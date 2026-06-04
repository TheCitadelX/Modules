using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Modules.Abstractions;
using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.AmneziaWGNodeModule;

public sealed class AmneziaWGNodeServer : INodeServer
{
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILogger<AmneziaWGNodeServer> _logger;
    private ServerLaunchProfile _profile;
    private RollingServerLog _log;
    private DateTimeOffset? _startedAt;
    private string? _lastStatusMessage;
    private ServerRuntimeHealth _health = ServerRuntimeHealth.Unknown;

    public AmneziaWGNodeServer(ServerLaunchProfile profile, AtomicFileWriter fileWriter, ILogger<AmneziaWGNodeServer> logger)
    {
        _profile = profile;
        _fileWriter = fileWriter;
        _logger = logger;
        _log = new RollingServerLog(ResolveLogPath(profile));
    }

    public bool IsRunning => ProbeRunning();

    public void ApplyProfile(ServerLaunchProfile profile)
    {
        _profile = profile;
        _log = new RollingServerLog(ResolveLogPath(profile));
    }

    public Task<ServerLogChunk> ReadLogsAsync(ServerLogQuery query)
        => _log.ReadAsync(query);

    public ServerRuntimeState GetRuntimeState()
    {
        var running = ProbeRunning();
        return new ServerRuntimeState
        {
            IsRunning = running,
            StartedAt = running ? _startedAt : null,
            Health = running ? ServerRuntimeHealth.Running : _health,
            StatusMessage = _lastStatusMessage
        };
    }

    public IReadOnlyList<ServerUserRuntimeSnapshot> GetUserRuntimeSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            return Array.Empty<ServerUserRuntimeSnapshot>();
        }

        var peers = AmneziaWGConfigDocument.Load(configPath).GetUserIdsByPublicKey();
        if (peers.Count == 0)
        {
            return Array.Empty<ServerUserRuntimeSnapshot>();
        }

        if (string.IsNullOrWhiteSpace(_profile.ServerId))
        {
            return Array.Empty<ServerUserRuntimeSnapshot>();
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return BuildUnavailableSnapshots(peers.Values, now, "AmneziaWG telemetry is available on Linux nodes only.");
        }

        var result = RunAsync("awg", new[] { "show", ResolveInterfaceName(), "dump" }, log: false).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
        {
            return BuildUnavailableSnapshots(peers.Values, now, result.Summary);
        }

        return ParseDump(result.Stdout, peers, now);
    }

    public async Task Start()
    {
        EnsureLinux();
        var configPath = GetConfigPath();
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("AmneziaWG config file does not exist.", configPath);
        }

        TryRestrictConfigPermissions(configPath);

        if (ProbeRunning())
        {
            _health = ServerRuntimeHealth.Running;
            return;
        }

        var result = await RunAsync("awg-quick", new[] { "up", configPath });
        if (result.ExitCode != 0)
        {
            _health = ServerRuntimeHealth.Failed;
            _lastStatusMessage = result.Summary;
            throw new InvalidOperationException($"awg-quick up failed: {result.Summary}");
        }

        _startedAt = DateTimeOffset.UtcNow;
        _health = ServerRuntimeHealth.Running;
        _lastStatusMessage = null;
        _logger.LogInformation("AmneziaWG interface started from {ConfigPath}.", configPath);
    }

    public async Task Stop()
    {
        EnsureLinux();
        if (!ProbeRunning())
        {
            _health = ServerRuntimeHealth.Stopped;
            _startedAt = null;
            return;
        }

        var result = await RunAsync("awg-quick", new[] { "down", GetConfigPath() });
        if (result.ExitCode != 0)
        {
            _health = ServerRuntimeHealth.Failed;
            _lastStatusMessage = result.Summary;
            throw new InvalidOperationException($"awg-quick down failed: {result.Summary}");
        }

        _startedAt = null;
        _health = ServerRuntimeHealth.Stopped;
        _lastStatusMessage = null;
        _logger.LogInformation("AmneziaWG interface stopped.");
    }

    public async Task Restart()
    {
        if (ProbeRunning())
        {
            await Stop();
        }

        await Start();
    }

    public async Task Apply(ConfigArtifact artifact)
    {
        if (artifact is not FileArtifact file)
        {
            throw new NotSupportedException($"AmneziaWG supports file artifacts only; received '{artifact.GetType().Name}'.");
        }

        if (string.IsNullOrWhiteSpace(file.Content))
        {
            throw new ArgumentException("AmneziaWG config content is empty.", nameof(artifact));
        }

        var configPath = GetConfigPath(file.FileName);
        _fileWriter.WriteAllTextAtomic(configPath, NormalizeConfig(file.Content));
        TryRestrictConfigPermissions(configPath);
        _profile.ConfigPath = configPath;

        if (ProbeRunning())
        {
            await Restart();
        }
        else
        {
            await Start();
        }
    }

    public async Task AddUser(UserEntity user, JsonObject? userTemplate = null)
    {
        var peer = AmneziaWGPeer.From(user, userTemplate);
        PatchConfig(config => config.UpsertPeer(peer));
        _log.Append("system", $"peer added: {user.Id}");
        await RestartIfRunning();
    }

    public async Task EditUser(UserEntity user, JsonObject? userTemplate = null)
    {
        var peer = AmneziaWGPeer.From(user, userTemplate);
        PatchConfig(config => config.UpsertPeer(peer));
        _log.Append("system", $"peer edited: {user.Id}");
        await RestartIfRunning();
    }

    public async Task RemoveUser(string userId)
    {
        PatchConfig(config => config.RemovePeer(userId));
        _log.Append("system", $"peer removed: {userId}");
        await RestartIfRunning();
    }

    public Task DisableUser(string userId)
        => RemoveUser(userId);

    public Task EnableUser(string userId)
        => throw new InvalidOperationException("AmneziaWG user.enable requires a userTemplate; send user.add instead.");

    public async Task SyncUsers(IReadOnlyCollection<string> allowedUserIds)
    {
        var allowed = new HashSet<string>(allowedUserIds, StringComparer.OrdinalIgnoreCase);
        PatchConfig(config => config.RemovePeersNotIn(allowed));
        _log.Append("system", $"peer sync complete: {allowed.Count} allowed");
        await RestartIfRunning();
    }

    private void PatchConfig(Action<AmneziaWGConfigDocument> patch)
    {
        var configPath = GetConfigPath();
        var document = AmneziaWGConfigDocument.Load(configPath);
        patch(document);
        _fileWriter.WriteAllTextAtomic(configPath, document.Serialize());
        TryRestrictConfigPermissions(configPath);
    }

    private async Task RestartIfRunning()
    {
        if (ProbeRunning())
        {
            await Restart();
        }
    }

    private bool ProbeRunning()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _health = ServerRuntimeHealth.Stopped;
            _lastStatusMessage = "AmneziaWG awg-quick runtime is Linux-only.";
            return false;
        }

        try
        {
            var result = RunAsync("awg", new[] { "show", ResolveInterfaceName() }, log: false).GetAwaiter().GetResult();
            if (result.ExitCode == 0)
            {
                _health = ServerRuntimeHealth.Running;
                _lastStatusMessage = null;
                return true;
            }

            _health = ServerRuntimeHealth.Stopped;
            _lastStatusMessage = string.IsNullOrWhiteSpace(result.Summary) ? null : result.Summary;
            return false;
        }
        catch (Exception ex)
        {
            _health = ServerRuntimeHealth.Failed;
            _lastStatusMessage = ex.Message;
            return false;
        }
    }

    private async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args, bool log = true)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
                if (log)
                {
                    _log.Append("stdout", e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
                if (log)
                {
                    _log.Append("stderr", e.Data);
                }
            }
        };

        if (log)
        {
            _log.Append("system", $"exec: {fileName} {string.Join(' ', args)}");
        }
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start '{fileName}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void EnsureLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("AmneziaWG awg-quick runtime is supported on Linux nodes only.");
        }
    }

    private string GetConfigPath(string? artifactFileName = null)
    {
        if (!string.IsNullOrWhiteSpace(artifactFileName))
        {
            var fileName = Path.GetFileName(artifactFileName);
            return Path.Combine(AppContext.BaseDirectory, "data", "amneziawg", fileName);
        }

        if (!string.IsNullOrWhiteSpace(_profile.ConfigPath))
        {
            return Path.GetFullPath(_profile.ConfigPath);
        }

        return Path.Combine(AppContext.BaseDirectory, "data", "amneziawg", "awg0.conf");
    }

    private string ResolveInterfaceName()
    {
        var configPath = GetConfigPath();
        var name = Path.GetFileNameWithoutExtension(configPath);
        return string.IsNullOrWhiteSpace(name) ? "awg0" : name;
    }

    private static string ResolveLogPath(ServerLaunchProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LogPath))
        {
            return profile.LogPath;
        }

        var safeId = string.IsNullOrWhiteSpace(profile.ServerId)
            ? "amneziawg"
            : string.Concat(profile.ServerId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return Path.Combine(AppContext.BaseDirectory, "data", "server-logs", $"{safeId}.jsonl");
    }

    private static string NormalizeConfig(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;

    private static void TryRestrictConfigPermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Permission hardening is best-effort; wg-quick will still surface a runtime warning if it fails.
        }
    }

    private IReadOnlyList<ServerUserRuntimeSnapshot> BuildUnavailableSnapshots(
        IEnumerable<string> userIds,
        DateTimeOffset now,
        string? message)
    {
        return userIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(userId => new ServerUserRuntimeSnapshot
            {
                ServerId = _profile.ServerId!,
                UserId = userId,
                IsOnline = false,
                Health = ServerUserTelemetryHealth.Unavailable,
                StatusMessage = string.IsNullOrWhiteSpace(message) ? null : message,
                ReportedAt = now
            })
            .ToArray();
    }

    private IReadOnlyList<ServerUserRuntimeSnapshot> ParseDump(
        string stdout,
        IReadOnlyDictionary<string, string> peers,
        DateTimeOffset now)
    {
        var rows = stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(parts => parts.Length >= 8)
            .ToDictionary(parts => parts[0], parts => parts, StringComparer.Ordinal);

        var snapshots = new List<ServerUserRuntimeSnapshot>();
        foreach (var peer in peers)
        {
            if (!rows.TryGetValue(peer.Key, out var parts))
            {
                snapshots.Add(new ServerUserRuntimeSnapshot
                {
                    ServerId = _profile.ServerId!,
                    UserId = peer.Value,
                    IsOnline = false,
                    Health = ServerUserTelemetryHealth.Offline,
                    StatusMessage = "Peer is present in config but not reported by awg.",
                    ReportedAt = now
                });
                continue;
            }

            var latestHandshake = ParseLong(parts[4]);
            var rxBytes = ParseLong(parts[5]);
            var txBytes = ParseLong(parts[6]);
            var lastSeenAt = latestHandshake > 0
                ? DateTimeOffset.FromUnixTimeSeconds(latestHandshake)
                : (DateTimeOffset?)null;
            var online = lastSeenAt is not null && now - lastSeenAt.Value <= TimeSpan.FromMinutes(3);

            snapshots.Add(new ServerUserRuntimeSnapshot
            {
                ServerId = _profile.ServerId!,
                UserId = peer.Value,
                IsOnline = online,
                LastSeenAt = lastSeenAt,
                RxBytes = rxBytes,
                TxBytes = txBytes,
                TrafficBytes = rxBytes + txBytes,
                Health = online
                    ? ServerUserTelemetryHealth.Online
                    : lastSeenAt is null
                        ? ServerUserTelemetryHealth.Offline
                        : ServerUserTelemetryHealth.Idle,
                ReportedAt = now
            });
        }

        return snapshots;
    }

    private static long ParseLong(string value)
        => long.TryParse(value, out var parsed) ? Math.Max(0, parsed) : 0;

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Summary => string.Join(" ", new[] { Stderr.Trim(), Stdout.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
