using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Modules.Abstractions;
using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.SlipstreamNodeModule;

public sealed class SlipstreamNodeServer : INodeServer
{
    private readonly object _sync = new();
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILogger<SlipstreamNodeServer> _logger;
    private ServerLaunchProfile _profile;
    private RollingServerLog _log;
    private Process? _process;
    private Process? _sidecarProcess;
    private DateTimeOffset? _startedAt;
    private int? _lastExitCode;
    private int? _lastSidecarExitCode;
    private string? _lastStatusMessage;

    public SlipstreamNodeServer(ServerLaunchProfile profile, AtomicFileWriter fileWriter, ILogger<SlipstreamNodeServer> logger)
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
                var slipstreamRunning = _process is not null && !_process.HasExited;
                if (!slipstreamRunning)
                {
                    return false;
                }

                return _lastSidecarExitCode is null && (_sidecarProcess is null || !_sidecarProcess.HasExited);
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
            var sidecarFailed = _lastSidecarExitCode.HasValue || (_sidecarProcess is not null && _sidecarProcess.HasExited);
            var effectiveRunning = running && !sidecarFailed;
            return new ServerRuntimeState
            {
                IsRunning = effectiveRunning,
                StartedAt = effectiveRunning ? _startedAt : null,
                Health = running
                    ? sidecarFailed
                        ? ServerRuntimeHealth.Failed
                        : ServerRuntimeHealth.Running
                    : _lastExitCode is > 0
                        ? ServerRuntimeHealth.Failed
                        : ServerRuntimeHealth.Stopped,
                StatusMessage = effectiveRunning ? null : _lastStatusMessage
            };
        }
    }

    public async Task Apply(ConfigArtifact artifact)
    {
        if (artifact is not FileArtifact file)
        {
            throw new NotSupportedException($"Slipstream supports file artifacts only; received '{artifact.GetType().Name}'.");
        }

        if (string.IsNullOrWhiteSpace(file.Content))
        {
            throw new ArgumentException("Slipstream config content is empty.", nameof(artifact));
        }

        var baseDir = SlipstreamConfigPaths.ResolveManagedDirectory(_profile, AppContext.BaseDirectory);
        Directory.CreateDirectory(baseDir);

        var configPath = Path.Combine(baseDir, "slipstream.conf");
        _fileWriter.WriteAllTextAtomic(configPath, Normalize(file.Content));

        lock (_sync)
        {
            _profile.ConfigPath = configPath;
        }

        if (IsRunning)
        {
            await Restart();
        }
        else
        {
            await Start();
        }
    }

    public Task Start()
    {
        lock (_sync)
        {
            var configPath = RequireConfigPath();
            var baseDir = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
            var settings = SlipstreamSettings.Parse(File.ReadAllText(configPath), baseDir);
            ValidateSettings(settings);

            var slipstreamRunning = _process is not null && !_process.HasExited;
            var sidecarRunning = _sidecarProcess is not null && !_sidecarProcess.HasExited;
            var effectiveRunning = slipstreamRunning
                                   && _lastSidecarExitCode is null
                                   && (!settings.SidecarEnabled || sidecarRunning);
            if (effectiveRunning)
            {
                return Task.CompletedTask;
            }

            if (slipstreamRunning || sidecarRunning)
            {
                StopProcess(_process);
                StopProcess(_sidecarProcess);
                _process?.Dispose();
                _sidecarProcess?.Dispose();
                _process = null;
                _sidecarProcess = null;
            }

            try
            {
                if (settings.SidecarEnabled)
                {
                    _sidecarProcess = StartSidecar(settings, baseDir);
                }

                _process = StartSlipstream(settings, baseDir);
                _startedAt = DateTimeOffset.UtcNow;
                _lastExitCode = null;
                _lastSidecarExitCode = null;
                _lastStatusMessage = null;
                _logger.LogInformation("Slipstream server started. Domain={Domain} Target={Target}", settings.Domain, settings.EffectiveTargetAddress);
            }
            catch
            {
                StopProcess(_sidecarProcess);
                _sidecarProcess = null;
                throw;
            }
        }

        return Task.CompletedTask;
    }

    private Process StartSlipstream(SlipstreamSettings settings, string baseDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settings.CertPath) ?? baseDir);
        Directory.CreateDirectory(Path.GetDirectoryName(settings.KeyPath) ?? baseDir);
        Directory.CreateDirectory(Path.GetDirectoryName(settings.ResetSeedPath) ?? baseDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveBinaryPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = baseDir
        };

        startInfo.ArgumentList.Add("--dns-listen-host");
        startInfo.ArgumentList.Add(settings.DnsListenHost);
        startInfo.ArgumentList.Add("--dns-listen-port");
        startInfo.ArgumentList.Add(settings.DnsListenPort.ToString());
        startInfo.ArgumentList.Add("--target-address");
        startInfo.ArgumentList.Add(settings.EffectiveTargetAddress);
        startInfo.ArgumentList.Add("--domain");
        startInfo.ArgumentList.Add(settings.Domain);
        startInfo.ArgumentList.Add("--cert");
        startInfo.ArgumentList.Add(settings.CertPath);
        startInfo.ArgumentList.Add("--key");
        startInfo.ArgumentList.Add(settings.KeyPath);
        startInfo.ArgumentList.Add("--reset-seed");
        startInfo.ArgumentList.Add(settings.ResetSeedPath);
        startInfo.ArgumentList.Add("--max-connections");
        startInfo.ArgumentList.Add(settings.MaxConnections.ToString());
        startInfo.ArgumentList.Add("--idle-timeout-seconds");
        startInfo.ArgumentList.Add(settings.IdleTimeoutSeconds.ToString());

        if (!string.IsNullOrWhiteSpace(settings.FallbackUdp))
        {
            startInfo.ArgumentList.Add("--fallback");
            startInfo.ArgumentList.Add(settings.FallbackUdp);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += OnProcessExited;
        process.OutputDataReceived += (_, e) => _log.Append("stdout", e.Data);
        process.ErrorDataReceived += (_, e) => _log.Append("stderr", e.Data);

        _log.Append("system", $"exec: {startInfo.FileName} --dns-listen-host {settings.DnsListenHost} --dns-listen-port {settings.DnsListenPort} --target-address {settings.EffectiveTargetAddress} --domain {settings.Domain}");
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start slipstream-server process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _log.Append("system", $"slipstream started pid={process.Id}");
        TryRestrictKeyPermissions(settings.KeyPath);
        TryRestrictKeyPermissions(settings.ResetSeedPath);
        return process;
    }

    private Process StartSidecar(SlipstreamSettings settings, string baseDir)
    {
        var binary = ResolveSidecarBinaryPath(settings);
        var configPath = Path.Combine(baseDir, "sidecar.sing-box.json");
        _fileWriter.WriteAllTextAtomic(configPath, BuildSidecarConfig(settings));

        var startInfo = new ProcessStartInfo
        {
            FileName = binary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = baseDir
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configPath);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += OnSidecarExited;
        process.OutputDataReceived += (_, e) => _log.Append("sidecar-stdout", e.Data);
        process.ErrorDataReceived += (_, e) => _log.Append("sidecar-stderr", e.Data);

        _log.Append("system", $"exec sidecar: {binary} run -c {configPath}");
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Slipstream sing-box sidecar process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _log.Append("system", $"sidecar started pid={process.Id} listen={settings.SidecarListen}");
        return process;
    }

    public async Task Stop()
    {
        Process? process;
        Process? sidecarProcess;
        lock (_sync)
        {
            process = _process;
            sidecarProcess = _sidecarProcess;
        }

        await StopProcessAsync(process, "slipstream");
        await StopProcessAsync(sidecarProcess, "sidecar");

        lock (_sync)
        {
            _log.Append("system", "stopped");
            _process?.Dispose();
            _sidecarProcess?.Dispose();
            _process = null;
            _sidecarProcess = null;
            _startedAt = null;
            _lastExitCode = null;
            _lastSidecarExitCode = null;
            _lastStatusMessage = null;
        }
    }

    public async Task Restart()
    {
        await Stop();
        await Start();
    }

    public Task AddUser(UserEntity user, JsonObject? userTemplate = null)
    {
        _log.Append("system", $"access attachment added: {user.Id}");
        return Task.CompletedTask;
    }

    public Task EditUser(UserEntity user, JsonObject? userTemplate = null)
    {
        _log.Append("system", $"access attachment edited: {user.Id}");
        return Task.CompletedTask;
    }

    public Task RemoveUser(string userId)
    {
        _log.Append("system", $"access attachment removed: {userId}");
        return Task.CompletedTask;
    }

    public Task DisableUser(string userId)
        => RemoveUser(userId);

    public Task EnableUser(string userId)
    {
        _log.Append("system", $"access attachment enabled: {userId}");
        return Task.CompletedTask;
    }

    public Task SyncUsers(IReadOnlyCollection<string> allowedUserIds)
    {
        _log.Append("system", $"access attachment sync: {allowedUserIds.Count} allowed");
        return Task.CompletedTask;
    }

    private string RequireConfigPath()
    {
        var configured = _profile.ConfigPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var managed = SlipstreamConfigPaths.ResolveManagedConfigPath(_profile, AppContext.BaseDirectory);
        if (File.Exists(managed))
        {
            return managed;
        }

        var legacyDirectory = SlipstreamConfigPaths.ResolveExistingOrManagedDirectory(_profile, AppContext.BaseDirectory);
        var legacy = Path.Combine(legacyDirectory, "slipstream.conf");
        if (File.Exists(legacy))
        {
            return legacy;
        }

        throw new FileNotFoundException("Slipstream config file is missing.", configured);
    }

    private string ResolveBinaryPath()
        => string.IsNullOrWhiteSpace(_profile.BinaryPath) ? "slipstream-server" : _profile.BinaryPath;

    private string ResolveSidecarBinaryPath(SlipstreamSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SidecarBinaryPath))
        {
            return settings.SidecarBinaryPath;
        }

        return ResolveBinaryOnPath("sing-box")
               ?? ResolveInstalledCoreBinary("Singbox")
               ?? ResolveInstalledCoreBinary("SingboxExtended")
               ?? "sing-box";
    }

    private static string? ResolveBinaryOnPath(string binaryName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), binaryName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static string? ResolveInstalledCoreBinary(string coreId)
    {
        var registryPath = Path.Combine(AppContext.BaseDirectory, "cores", "registry.json");
        if (!File.Exists(registryPath))
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(registryPath)) as JsonArray;
            if (root is null)
            {
                return null;
            }

            foreach (var item in root.OfType<JsonObject>())
            {
                var itemCoreId = ReadString(item, "coreId");
                var binaryPath = ReadString(item, "binaryPath");
                if (string.Equals(itemCoreId, coreId, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(binaryPath)
                    && File.Exists(binaryPath))
                {
                    return binaryPath;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string BuildSidecarConfig(SlipstreamSettings settings)
    {
        var (listenHost, listenPort) = SplitListen(settings.SidecarListen, "127.0.0.1", 10818);
        var inbound = new JsonObject
        {
            ["type"] = settings.SidecarInboundType,
            ["tag"] = "slipstream-sidecar-in",
            ["listen"] = listenHost,
            ["listen_port"] = listenPort
        };

        if (settings.SidecarAuthEnabled)
        {
            inbound["users"] = new JsonArray
            {
                new JsonObject
                {
                    ["username"] = settings.SidecarUsername,
                    ["password"] = settings.SidecarPassword
                }
            };
        }

        var outboundTag = settings.SidecarOutbound == "block" ? "block" : "direct";
        var root = new JsonObject
        {
            ["log"] = new JsonObject
            {
                ["level"] = settings.SidecarLogLevel
            },
            ["inbounds"] = new JsonArray { inbound },
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = outboundTag,
                    ["tag"] = outboundTag
                }
            },
            ["route"] = new JsonObject
            {
                ["final"] = outboundTag
            }
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static (string Host, int Port) SplitListen(string value, string fallbackHost, int fallbackPort)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (fallbackHost, fallbackPort);
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            var end = trimmed.IndexOf(']');
            if (end > 0 && trimmed.Length > end + 2 && trimmed[end + 1] == ':' && int.TryParse(trimmed[(end + 2)..], out var ipv6Port))
            {
                return (trimmed[1..end], ipv6Port);
            }
        }

        var index = trimmed.LastIndexOf(':');
        if (index < 0)
        {
            return (fallbackHost, int.TryParse(trimmed, out var onlyPort) ? onlyPort : fallbackPort);
        }

        var host = trimmed[..index];
        var portText = trimmed[(index + 1)..];
        var port = int.TryParse(portText, out var parsedPort) ? parsedPort : fallbackPort;
        return (string.IsNullOrWhiteSpace(host) ? fallbackHost : host, port);
    }

    private static void ValidateSettings(SlipstreamSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Domain))
        {
            throw new InvalidOperationException("Slipstream domain is required.");
        }

        if (settings.DnsListenPort <= 0)
        {
            throw new InvalidOperationException("Slipstream UDP listen port is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.EffectiveTargetAddress))
        {
            throw new InvalidOperationException("Slipstream target address is required.");
        }

        if (settings.SidecarEnabled && string.IsNullOrWhiteSpace(settings.SidecarListen))
        {
            throw new InvalidOperationException("Slipstream sidecarListen is required when SOCKS5 sidecar mode is enabled.");
        }
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

    private void OnSidecarExited(object? sender, EventArgs args)
    {
        if (sender is not Process process)
        {
            return;
        }

        lock (_sync)
        {
            _lastSidecarExitCode = TryGetExitCode(process);
            _lastStatusMessage = _lastSidecarExitCode is > 0
                ? $"SOCKS5 sidecar exited with code {_lastSidecarExitCode.Value}."
                : "SOCKS5 sidecar exited.";
            _log.Append("system", _lastSidecarExitCode.HasValue ? $"sidecar exited code={_lastSidecarExitCode.Value}" : "sidecar exited");
            if (_process is not null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Runtime state will still report failed/stopped on the next probe.
                }
            }

            _sidecarProcess?.Dispose();
            _sidecarProcess = null;
        }
    }

    private static void StopProcess(Process? process)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup during failed starts.
        }
    }

    private async Task StopProcessAsync(Process? process, string name)
    {
        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            await WaitForExitAsync(process, TimeSpan.FromSeconds(10));
            _log.Append("system", $"{name} stopped");
        }
        catch (Exception ex)
        {
            _log.Append("system", $"{name} stop failed: {ex.Message}");
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

    private static void TryRestrictKeyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private static string ResolveLogPath(ServerLaunchProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LogPath))
        {
            return profile.LogPath;
        }

        var safeId = string.IsNullOrWhiteSpace(profile.ServerId)
            ? "slipstream"
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

    private static string? ReadString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return node.ToString();
        }
    }

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;

    private sealed class SlipstreamSettings
    {
        public required string Domain { get; init; }
        public required string DnsListenHost { get; init; }
        public required int DnsListenPort { get; init; }
        public required string ForwardMode { get; init; }
        public required string TargetAddress { get; init; }
        public required string SidecarListen { get; init; }
        public required string SidecarInboundType { get; init; }
        public required string SidecarOutbound { get; init; }
        public bool SidecarAuthEnabled { get; init; }
        public required string SidecarUsername { get; init; }
        public required string SidecarPassword { get; init; }
        public string SidecarBinaryPath { get; init; } = string.Empty;
        public required string SidecarLogLevel { get; init; }
        public required string CertPath { get; init; }
        public required string KeyPath { get; init; }
        public required string ResetSeedPath { get; init; }
        public int MaxConnections { get; init; }
        public int IdleTimeoutSeconds { get; init; }
        public string FallbackUdp { get; init; } = string.Empty;
        public bool SidecarEnabled => ForwardMode == "socks5Sidecar";
        public string EffectiveTargetAddress => SidecarEnabled ? SidecarListen : TargetAddress;

        public static SlipstreamSettings Parse(string content, string baseDir)
        {
            var values = ParseKeyValues(content);
            var (listenHost, listenPort) = SplitListen(Get(values, "udpListen", ":53"), "::", 53);
            return new SlipstreamSettings
            {
                Domain = Get(values, "domain", "slip.example.com"),
                DnsListenHost = listenHost,
                DnsListenPort = listenPort,
                ForwardMode = NormalizeForwardMode(Get(values, "forwardMode", "socks5Sidecar")),
                TargetAddress = Get(values, "targetAddress", "127.0.0.1:22"),
                SidecarListen = Get(values, "sidecarListen", "127.0.0.1:10818"),
                SidecarInboundType = NormalizeSidecarInboundType(Get(values, "sidecarInboundType", "mixed")),
                SidecarOutbound = NormalizeSidecarOutbound(Get(values, "sidecarOutbound", "direct")),
                SidecarAuthEnabled = IsTrue(Get(values, "sidecarAuthEnabled", "false")),
                SidecarUsername = Get(values, "sidecarUsername", "slipstream"),
                SidecarPassword = Get(values, "sidecarPassword", string.Empty),
                SidecarBinaryPath = Get(values, "sidecarBinaryPath", string.Empty),
                SidecarLogLevel = NormalizeLogLevel(Get(values, "sidecarLogLevel", "info")),
                CertPath = ResolvePath(baseDir, Get(values, "certPath", "cert.pem")),
                KeyPath = ResolvePath(baseDir, Get(values, "keyPath", "key.pem")),
                ResetSeedPath = ResolvePath(baseDir, Get(values, "resetSeedPath", "reset-seed")),
                MaxConnections = GetInt(values, "maxConnections", 256),
                IdleTimeoutSeconds = GetInt(values, "idleTimeoutSeconds", 60),
                FallbackUdp = Get(values, "fallbackUdp", string.Empty)
            };
        }

        private static Dictionary<string, string> ParseKeyValues(string content)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in Normalize(content).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 2)
                {
                    result[parts[0]] = Unquote(parts[1]);
                }
            }

            return result;
        }

        private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
            => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

        private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
            => values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

        private static string NormalizeForwardMode(string value)
            => value.Equals("rawTcp", StringComparison.OrdinalIgnoreCase) ? "rawTcp" : "socks5Sidecar";

        private static string NormalizeSidecarInboundType(string value)
            => value.Equals("socks", StringComparison.OrdinalIgnoreCase) ? "socks" : "mixed";

        private static string NormalizeSidecarOutbound(string value)
            => value.Equals("block", StringComparison.OrdinalIgnoreCase) ? "block" : "direct";

        private static string NormalizeLogLevel(string value)
            => value.Equals("debug", StringComparison.OrdinalIgnoreCase)
                ? "debug"
                : value.Equals("warn", StringComparison.OrdinalIgnoreCase)
                    ? "warn"
                    : value.Equals("error", StringComparison.OrdinalIgnoreCase)
                        ? "error"
                        : "info";

        private static bool IsTrue(string value)
            => value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("1", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

        private static string ResolvePath(string baseDir, string path)
            => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(baseDir, path));

        private static string Unquote(string value)
        {
            var trimmed = value.Trim();
            return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
                ? trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
                : trimmed;
        }
    }
}
