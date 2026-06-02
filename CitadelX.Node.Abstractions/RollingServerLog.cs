using System.Text.Json;
using CitadelX.Modules.Abstractions;

namespace CitadelX.Node.Abstractions;

public sealed class RollingServerLog
{
    private const int DefaultLimit = 200;
    private const int MaxLimit = 2_000;
    private const long DefaultMaxBytes = 1_048_576;

    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly long _maxBytes;
    private long _nextOffset;

    public RollingServerLog(string path, long maxBytes = DefaultMaxBytes)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Log path is required.", nameof(path));
        }

        _path = System.IO.Path.GetFullPath(path);
        _maxBytes = Math.Max(64 * 1024, maxBytes);
        _nextOffset = LoadNextOffset();
    }

    public string Path => _path;

    public void Append(string stream, string? text)
    {
        if (text is null)
        {
            return;
        }

        var lines = SplitLines(text);
        if (lines.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path) ?? AppContext.BaseDirectory);
            using var writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read));
            foreach (var line in lines)
            {
                var entry = new ServerLogLine
                {
                    Offset = _nextOffset++,
                    Timestamp = DateTimeOffset.UtcNow,
                    Stream = NormalizeStream(stream),
                    Text = line
                };
                writer.WriteLine(JsonSerializer.Serialize(entry, _jsonOptions));
            }
            writer.Flush();
            TrimIfNeeded();
        }
    }

    public Task<ServerLogChunk> ReadAsync(ServerLogQuery? query)
    {
        lock (_sync)
        {
            return Task.FromResult(ReadLocked(query ?? new ServerLogQuery()));
        }
    }

    private ServerLogChunk ReadLocked(ServerLogQuery query)
    {
        if (!File.Exists(_path))
        {
            return new ServerLogChunk();
        }

        var stream = NormalizeStream(query.Stream);
        var limit = Math.Clamp(query.Limit <= 0 ? DefaultLimit : query.Limit, 1, MaxLimit);
        var matching = new List<ServerLogLine>();

        foreach (var raw in File.ReadLines(_path))
        {
            ServerLogLine? line;
            try
            {
                line = JsonSerializer.Deserialize<ServerLogLine>(raw, _jsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (line is null)
            {
                continue;
            }

            if (query.Since.HasValue && line.Offset <= query.Since.Value)
            {
                continue;
            }

            if (stream != "all" && !string.Equals(line.Stream, stream, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matching.Add(line);
        }

        var truncated = matching.Count > limit;
        var lines = query.Since.HasValue
            ? matching.Take(limit).ToList()
            : matching.Skip(Math.Max(0, matching.Count - limit)).ToList();

        return new ServerLogChunk
        {
            Lines = lines,
            NextOffset = lines.Count > 0 ? lines[^1].Offset : query.Since,
            Truncated = truncated
        };
    }

    private long LoadNextOffset()
    {
        if (!File.Exists(_path))
        {
            return 0;
        }

        string? last = null;
        foreach (var line in File.ReadLines(_path))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                last = line;
            }
        }

        if (last is null)
        {
            return 0;
        }

        try
        {
            var entry = JsonSerializer.Deserialize<ServerLogLine>(last, _jsonOptions);
            return (entry?.Offset ?? -1) + 1;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private void TrimIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length <= _maxBytes)
        {
            return;
        }

        var lines = File.ReadAllLines(_path);
        var keep = new List<string>();
        var bytes = 0L;
        var targetBytes = _maxBytes / 2;

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var nextBytes = System.Text.Encoding.UTF8.GetByteCount(lines[i]) + Environment.NewLine.Length;
            if (keep.Count > 0 && bytes + nextBytes > targetBytes)
            {
                break;
            }

            keep.Add(lines[i]);
            bytes += nextBytes;
        }

        keep.Reverse();
        File.WriteAllLines(_path, keep);
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Split('\n');
    }

    private static string NormalizeStream(string? stream)
    {
        return stream?.Trim().ToLowerInvariant() switch
        {
            "stdout" => "stdout",
            "stderr" => "stderr",
            "system" => "system",
            _ => "all"
        };
    }
}
