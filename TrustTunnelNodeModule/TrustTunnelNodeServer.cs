using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Modules.Abstractions;
using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.TrustTunnelNodeModule;

public sealed class TrustTunnelNodeServer : INodeServer
{
    private const string BundleMarker = "# CitadelX-File:";
    private const string DisabledPlaceholderUsername = "__citadelx_disabled__";
    private readonly object _sync = new();
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILogger<TrustTunnelNodeServer> _logger;
    private ServerLaunchProfile _profile;
    private Process? _process;
    private RollingServerLog _log;
    private DateTimeOffset? _startedAt;
    private int? _lastExitCode;
    private string? _lastStatusMessage;

    public TrustTunnelNodeServer(ServerLaunchProfile profile, AtomicFileWriter fileWriter, ILogger<TrustTunnelNodeServer> logger)
    {
        _profile = profile;
        _fileWriter = fileWriter;
        _logger = logger;
        _log = new RollingServerLog(ResolveLogPath(profile));
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is not null && !_process.HasExited;
            }
        }
    }

    public void ApplyProfile(ServerLaunchProfile profile)
    {
        lock (_sync)
        {
            _profile = profile;
            _log = new RollingServerLog(ResolveLogPath(profile));
        }
    }

    public Task<ServerLogChunk> ReadLogsAsync(ServerLogQuery query)
        => _log.ReadAsync(query);

    public Task ClearLogsAsync()
        => _log.ClearAsync();

    public ServerRuntimeState GetRuntimeState()
    {
        lock (_sync)
        {
            var running = _process is not null && !_process.HasExited;
            return new ServerRuntimeState
            {
                IsRunning = running,
                StartedAt = running ? _startedAt : null,
                Health = running
                    ? ServerRuntimeHealth.Running
                    : _lastExitCode is > 0
                        ? ServerRuntimeHealth.Failed
                        : ServerRuntimeHealth.Stopped,
                StatusMessage = running ? null : _lastStatusMessage
            };
        }
    }

    public Task Apply(ConfigArtifact artifact)
    {
        if (artifact is not FileArtifact file)
        {
            throw new NotSupportedException($"TrustTunnel supports file artifacts only; received '{artifact.GetType().Name}'.");
        }

        var files = ParseBundle(file.Content);
        if (files.Count == 0)
        {
            throw new InvalidOperationException("TrustTunnel artifact does not contain bundled config files.");
        }

        var baseDir = ResolveManagedConfigDirectory();
        Directory.CreateDirectory(baseDir);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(baseDir, Path.GetFileName(name));
            _fileWriter.WriteAllTextAtomic(path, Normalize(content));
        }

        _profile.ConfigPath = baseDir;
        lock (_sync)
        {
            _profile.ConfigPath = baseDir;
        }

        return IsRunning ? Restart() : Start();
    }

    public Task Start()
    {
        lock (_sync)
        {
            if (_process is not null && !_process.HasExited)
            {
                return Task.CompletedTask;
            }

            var binaryPath = RequireBinaryPath();
            var baseDir = ResolveConfigDirectory();
            var vpnPath = Path.Combine(baseDir, "vpn.toml");
            var hostsPath = Path.Combine(baseDir, "hosts.toml");
            if (!File.Exists(vpnPath) || !File.Exists(hostsPath))
            {
                throw new FileNotFoundException("TrustTunnel vpn.toml/hosts.toml files are missing.", baseDir);
            }

            ValidateTlsFiles(baseDir, hostsPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = baseDir
            };
            startInfo.ArgumentList.Add(vpnPath);
            startInfo.ArgumentList.Add(hostsPath);
            var logLevel = ExtractComment(File.ReadAllText(vpnPath), "CitadelX-LogLevel");
            if (!string.IsNullOrWhiteSpace(logLevel))
            {
                startInfo.ArgumentList.Add("-l");
                startInfo.ArgumentList.Add(logLevel);
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;
            process.OutputDataReceived += (_, e) => _log.Append("stdout", e.Data);
            process.ErrorDataReceived += (_, e) => _log.Append("stderr", e.Data);
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start TrustTunnel endpoint process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _startedAt = DateTimeOffset.UtcNow;
            _lastExitCode = null;
            _lastStatusMessage = null;
            _log.Append("system", $"started pid={process.Id}");
            _logger.LogInformation("TrustTunnel endpoint started. ConfigDir={ConfigDir}", baseDir);
        }

        return Task.CompletedTask;
    }

    public async Task Stop()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
        }

        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            await WaitForExitAsync(process, TimeSpan.FromSeconds(10));
        }
        finally
        {
            lock (_sync)
            {
                _log.Append("system", "stopped");
                _process?.Dispose();
                _process = null;
                _startedAt = null;
                _lastExitCode = null;
                _lastStatusMessage = null;
            }
        }
    }

    public async Task Restart()
    {
        await Stop();
        await Start();
    }

    public async Task AddUser(UserEntity user, JsonObject? userTemplate = null)
    {
        PatchCredentials(clients => UpsertClient(clients, user.Id, userTemplate));
        _log.Append("system", $"client added: {user.Id}");
        if (IsRunning)
        {
            await Restart();
        }
    }

    public async Task EditUser(UserEntity user, JsonObject? userTemplate = null)
    {
        PatchCredentials(clients => UpsertClient(clients, user.Id, userTemplate));
        _log.Append("system", $"client edited: {user.Id}");
        if (IsRunning)
        {
            await Restart();
        }
    }

    public async Task RemoveUser(string userId)
    {
        PatchCredentials(clients => clients.RemoveAll(item => string.Equals(item.Username, userId, StringComparison.OrdinalIgnoreCase)));
        _log.Append("system", $"client removed: {userId}");
        if (IsRunning)
        {
            await Restart();
        }
    }

    public Task DisableUser(string userId)
        => RemoveUser(userId);

    public Task EnableUser(string userId)
        => throw new InvalidOperationException("TrustTunnel user.enable requires a userTemplate; send user.add instead.");

    public async Task SyncUsers(IReadOnlyCollection<string> allowedUserIds)
    {
        var allowed = new HashSet<string>(allowedUserIds, StringComparer.OrdinalIgnoreCase);
        PatchCredentials(clients => clients.RemoveAll(item => !allowed.Contains(item.Username)));
        _log.Append("system", $"client sync complete: {allowed.Count} allowed");
        if (IsRunning)
        {
            await Restart();
        }
    }

    private void PatchCredentials(Action<List<TrustTunnelClient>> patch)
    {
        var path = Path.Combine(ResolveConfigDirectory(), "credentials.toml");
        var clients = LoadClients(path);
        patch(clients);
        _fileWriter.WriteAllTextAtomic(path, SerializeClients(clients));
    }

    private static void UpsertClient(List<TrustTunnelClient> clients, string fallbackUsername, JsonObject? userTemplate)
    {
        var username = GetString(userTemplate, "username", fallbackUsername);
        var password = GetString(userTemplate, "password", string.Empty);
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("TrustTunnel user template must contain password.");
        }

        clients.RemoveAll(item => string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
        clients.Add(new TrustTunnelClient(username, password));
    }

    private string RequireBinaryPath()
    {
        if (string.IsNullOrWhiteSpace(_profile.BinaryPath))
        {
            throw new InvalidOperationException("BinaryPath is not configured.");
        }

        return _profile.BinaryPath;
    }

    private string ResolveConfigDirectory()
        => TrustTunnelConfigPaths.ResolveExistingOrManagedDirectory(_profile, AppContext.BaseDirectory);

    private string ResolveManagedConfigDirectory()
        => TrustTunnelConfigPaths.ResolveManagedDirectory(_profile, AppContext.BaseDirectory);

    private static Dictionary<string, string> ParseBundle(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentName = string.Empty;
        var builder = new StringBuilder();
        foreach (var line in Normalize(content).Split('\n'))
        {
            if (line.StartsWith(BundleMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(currentName))
                {
                    result[currentName] = builder.ToString().TrimEnd() + Environment.NewLine;
                }

                currentName = line[BundleMarker.Length..].Trim();
                builder.Clear();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentName))
            {
                builder.AppendLine(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(currentName))
        {
            result[currentName] = builder.ToString().TrimEnd() + Environment.NewLine;
        }

        return result;
    }

    private static List<TrustTunnelClient> LoadClients(string path)
    {
        var result = new List<TrustTunnelClient>();
        if (!File.Exists(path))
        {
            return result;
        }

        string? username = null;
        string? password = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("[[client]]", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                continue;
            }

            if (line.StartsWith("username", StringComparison.OrdinalIgnoreCase))
            {
                username = ReadTomlString(line);
            }
            else if (line.StartsWith("password", StringComparison.OrdinalIgnoreCase))
            {
                password = ReadTomlString(line);
            }
        }

        Flush();
        return result;

        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
            {
                result.Add(new TrustTunnelClient(username, password));
            }

            username = null;
            password = null;
        }
    }

    private static string SerializeClients(IEnumerable<TrustTunnelClient> clients)
    {
        var ordered = clients
            .Where(item => !string.Equals(item.Username, DisabledPlaceholderUsername, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Username, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count == 0)
        {
            return $"""
            # CitadelX-managed clients. TrustTunnel requires at least one [[client]] entry.
            # This disabled placeholder is replaced when real users are attached.
            [[client]]
            username = "{DisabledPlaceholderUsername}"
            password = "{GeneratePassword()}"
            """;
        }

        var builder = new StringBuilder();
        foreach (var client in ordered)
        {
            builder.AppendLine("[[client]]");
            builder.Append("username = \"").Append(Toml(client.Username)).AppendLine("\"");
            builder.Append("password = \"").Append(Toml(client.Password)).AppendLine("\"");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        if (sender is not Process process)
        {
            return;
        }

        lock (_sync)
        {
            _lastExitCode = TryGetExitCode(process);
            _lastStatusMessage = _lastExitCode is > 0 ? $"Process exited with code {_lastExitCode.Value}." : null;
            _log.Append("system", _lastExitCode.HasValue ? $"exited code={_lastExitCode.Value}" : "exited");
            _process?.Dispose();
            _process = null;
            _startedAt = null;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private static string? ExtractComment(string content, string key)
    {
        var prefix = $"# {key}:";
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static void ValidateTlsFiles(string baseDir, string hostsPath)
    {
        string? certChainPath = null;
        string? privateKeyPath = null;

        foreach (var raw in File.ReadAllLines(hostsPath))
        {
            var line = raw.Trim();
            if (line.StartsWith("cert_chain_path", StringComparison.OrdinalIgnoreCase))
            {
                certChainPath = ReadTomlString(line);
            }
            else if (line.StartsWith("private_key_path", StringComparison.OrdinalIgnoreCase))
            {
                privateKeyPath = ReadTomlString(line);
            }
        }

        ValidateReadableFile(baseDir, certChainPath, "cert_chain_path");
        ValidateReadableFile(baseDir, privateKeyPath, "private_key_path");
    }

    private static void ValidateReadableFile(string baseDir, string? configuredPath, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException($"TrustTunnel hosts.toml must define {fieldName}.");
        }

        var fullPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(baseDir, configuredPath));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"TrustTunnel {fieldName} file does not exist.", fullPath);
        }

        try
        {
            using var stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (!stream.CanRead)
            {
                throw new InvalidOperationException($"TrustTunnel {fieldName} file is not readable: {fullPath}");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"TrustTunnel {fieldName} file is not readable: {fullPath}", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"TrustTunnel {fieldName} file is not readable: {fullPath}", ex);
        }
    }

    private static string? ReadTomlString(string line)
    {
        var idx = line.IndexOf('=');
        if (idx < 0)
        {
            return null;
        }

        return line[(idx + 1)..].Trim().Trim('"');
    }

    private static string GetString(JsonObject? obj, string key, string fallback)
        => obj is not null
           && obj.TryGetPropertyValue(key, out var node)
           && node is not null
           && node.GetValueKind() == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(node.GetValue<string>())
            ? node.GetValue<string>()
            : fallback;

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;

    private static string Toml(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ResolveLogPath(ServerLaunchProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LogPath))
        {
            return profile.LogPath;
        }

        var safeId = string.IsNullOrWhiteSpace(profile.ServerId)
            ? "trusttunnel"
            : string.Concat(profile.ServerId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(AppContext.BaseDirectory, "data", "server-logs", $"{safeId}.jsonl");
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record TrustTunnelClient(string Username, string Password);
}
