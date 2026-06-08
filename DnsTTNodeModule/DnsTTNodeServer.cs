using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Modules.Abstractions;
using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.DnsTTNodeModule;

public sealed class DnsTTNodeServer : INodeServer, INodeApplyReportProvider
{
    private readonly object _sync = new();
    private readonly AtomicFileWriter _fileWriter;
    private readonly ILogger<DnsTTNodeServer> _logger;
    private ServerLaunchProfile _profile;
    private RollingServerLog _log;
    private Process? _process;
    private Process? _sidecarProcess;
    private DateTimeOffset? _startedAt;
    private int? _lastExitCode;
    private int? _lastSidecarExitCode;
    private string? _lastStatusMessage;
    private JsonObject? _lastApplyReport;

    public DnsTTNodeServer(ServerLaunchProfile profile, AtomicFileWriter fileWriter, ILogger<DnsTTNodeServer> logger)
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
                var dnsttRunning = _process is not null && !_process.HasExited;
                if (!dnsttRunning)
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

    public JsonObject? GetLastApplyReport()
    {
        lock (_sync)
        {
            return _lastApplyReport is null
                ? null
                : JsonNode.Parse(_lastApplyReport.ToJsonString()) as JsonObject;
        }
    }

    public async Task Apply(ConfigArtifact artifact)
    {
        if (artifact is not FileArtifact file)
        {
            throw new NotSupportedException($"DnsTT supports file artifacts only; received '{artifact.GetType().Name}'.");
        }

        if (string.IsNullOrWhiteSpace(file.Content))
        {
            throw new ArgumentException("DnsTT config content is empty.", nameof(artifact));
        }

        var baseDir = ResolveConfigDirectory(file.FileName);
        Directory.CreateDirectory(baseDir);

        var configPath = Path.Combine(baseDir, "dnstt.conf");
        _fileWriter.WriteAllTextAtomic(configPath, Normalize(file.Content));
        var settings = DnsTTSettings.Parse(File.ReadAllText(configPath), baseDir);
        var keyInfo = await EnsureKeysAsync(settings);

        lock (_sync)
        {
            _profile.ConfigPath = configPath;
            _lastApplyReport = new JsonObject
            {
                ["dnstt"] = new JsonObject
                {
                    ["publicKey"] = keyInfo.PublicKey,
                    ["privateKeyFile"] = keyInfo.PrivateKeyFile,
                    ["publicKeyFile"] = keyInfo.PublicKeyFile
                }
            };
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
            var settings = DnsTTSettings.Parse(File.ReadAllText(configPath), baseDir);
            ValidateSettings(settings);
            var dnsttRunning = _process is not null && !_process.HasExited;
            var sidecarRunning = _sidecarProcess is not null && !_sidecarProcess.HasExited;
            var effectiveRunning = dnsttRunning
                                   && _lastSidecarExitCode is null
                                   && (!settings.SidecarEnabled || sidecarRunning);
            if (effectiveRunning)
            {
                return Task.CompletedTask;
            }

            if (dnsttRunning || sidecarRunning)
            {
                StopProcess(_process);
                StopProcess(_sidecarProcess);
                _process?.Dispose();
                _sidecarProcess?.Dispose();
                _process = null;
                _sidecarProcess = null;
            }

            if (!File.Exists(settings.PrivateKeyFile))
            {
                throw new FileNotFoundException("DnsTT private key file is missing.", settings.PrivateKeyFile);
            }

            try
            {
                if (settings.SidecarEnabled)
                {
                    _sidecarProcess = StartSidecar(settings, baseDir);
                }

                _process = StartDnsTT(settings, baseDir);
                _startedAt = DateTimeOffset.UtcNow;
                _lastExitCode = null;
                _lastSidecarExitCode = null;
                _lastStatusMessage = null;
                _logger.LogInformation("DnsTT server started. Domain={Domain} Target={Target}", settings.Domain, settings.EffectiveTargetAddress);
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

    private Process StartDnsTT(DnsTTSettings settings, string baseDir)
    {
        var startInfo = new ProcessStartInfo
            {
                FileName = ResolveBinaryPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = baseDir
            };
        startInfo.ArgumentList.Add("-udp");
        startInfo.ArgumentList.Add(settings.UdpListen);
        startInfo.ArgumentList.Add("-privkey-file");
        startInfo.ArgumentList.Add(settings.PrivateKeyFile);
        startInfo.ArgumentList.Add(settings.Domain);
        startInfo.ArgumentList.Add(settings.EffectiveTargetAddress);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += OnProcessExited;
        process.OutputDataReceived += (_, e) => _log.Append("stdout", e.Data);
        process.ErrorDataReceived += (_, e) => _log.Append("stderr", e.Data);

        _log.Append("system", $"exec: {startInfo.FileName} -udp {settings.UdpListen} -privkey-file {settings.PrivateKeyFile} {settings.Domain} {settings.EffectiveTargetAddress}");
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dnstt-server process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _log.Append("system", $"dnstt started pid={process.Id}");
        return process;
    }

    private Process StartSidecar(DnsTTSettings settings, string baseDir)
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
            throw new InvalidOperationException("Failed to start DnsTT sing-box sidecar process.");
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

        await StopProcessAsync(process, "dnstt");
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

    private async Task<DnsTTKeyInfo> EnsureKeysAsync(DnsTTSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settings.PrivateKeyFile) ?? AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(settings.PublicKeyFile) ?? AppContext.BaseDirectory);

        if (!File.Exists(settings.PrivateKeyFile))
        {
            var result = await RunAsync(
                ResolveBinaryPath(),
                new[] { "-gen-key", "-privkey-file", settings.PrivateKeyFile, "-pubkey-file", settings.PublicKeyFile });

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"dnstt-server key generation failed: {result.Summary}");
            }

            TryRestrictKeyPermissions(settings.PrivateKeyFile);
        }

        var publicKey = File.Exists(settings.PublicKeyFile)
            ? File.ReadAllText(settings.PublicKeyFile).Trim()
            : settings.ServerPublicKey;

        if (string.IsNullOrWhiteSpace(publicKey))
        {
            _log.Append("system", "public key file is missing; subscriptions will require manual serverPublicKey.");
        }

        return new DnsTTKeyInfo(settings.PrivateKeyFile, settings.PublicKeyFile, publicKey);
    }

    private async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> args)
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

        _log.Append("system", $"exec: {fileName} {string.Join(' ', args)}");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            _log.Append("stdout", stdout.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _log.Append("stderr", stderr.TrimEnd());
        }

        return new CommandResult(process.ExitCode, stdout, stderr);
    }

    private string RequireConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(_profile.ConfigPath) && File.Exists(_profile.ConfigPath))
        {
            return _profile.ConfigPath;
        }

        throw new FileNotFoundException("DnsTT config file is missing.", _profile.ConfigPath);
    }

    private string ResolveBinaryPath()
        => string.IsNullOrWhiteSpace(_profile.BinaryPath) ? "dnstt-server" : _profile.BinaryPath;

    private string ResolveSidecarBinaryPath(DnsTTSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SidecarBinaryPath))
        {
            return settings.SidecarBinaryPath;
        }

        var pathBinary = ResolveBinaryOnPath("sing-box");
        if (!string.IsNullOrWhiteSpace(pathBinary))
        {
            return pathBinary;
        }

        var installed = ResolveInstalledCoreBinary("Singbox") ?? ResolveInstalledCoreBinary("SingboxExtended");
        if (!string.IsNullOrWhiteSpace(installed))
        {
            return installed;
        }

        return "sing-box";
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

    private static string BuildSidecarConfig(DnsTTSettings settings)
    {
        var (listenHost, listenPort) = SplitListen(settings.SidecarListen, "127.0.0.1", 10808);
        var inbound = new JsonObject
        {
            ["type"] = settings.SidecarInboundType,
            ["tag"] = "dnstt-sidecar-in",
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

    private string ResolveConfigDirectory(string? artifactFileName = null)
    {
        var configured = _profile.ConfigPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(configured);
            return Path.HasExtension(full) ? Path.GetDirectoryName(full) ?? full : full;
        }

        var safeId = string.IsNullOrWhiteSpace(_profile.ServerId)
            ? "dnstt"
            : string.Concat(_profile.ServerId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        var fileName = string.IsNullOrWhiteSpace(artifactFileName) ? safeId : Path.GetFileNameWithoutExtension(artifactFileName);
        return Path.Combine(AppContext.BaseDirectory, "data", "dnstt", string.IsNullOrWhiteSpace(fileName) ? safeId : fileName);
    }

    private static void ValidateSettings(DnsTTSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Domain))
        {
            throw new InvalidOperationException("DnsTT domain is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.UdpListen))
        {
            throw new InvalidOperationException("DnsTT udpListen is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.TargetAddress))
        {
            throw new InvalidOperationException("DnsTT targetAddress is required.");
        }

        if (settings.SidecarEnabled && string.IsNullOrWhiteSpace(settings.SidecarListen))
        {
            throw new InvalidOperationException("DnsTT sidecarListen is required when SOCKS5 sidecar mode is enabled.");
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
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
            ? "dnstt"
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

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;

    private sealed record DnsTTKeyInfo(string PrivateKeyFile, string PublicKeyFile, string PublicKey);

    private sealed record CommandResult(int ExitCode, string Stdout, string Stderr)
    {
        public string Summary => string.Join(" ", new[] { Stderr.Trim(), Stdout.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private sealed class DnsTTSettings
    {
        public required string Domain { get; init; }
        public required string UdpListen { get; init; }
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
        public required string PrivateKeyFile { get; init; }
        public required string PublicKeyFile { get; init; }
        public string ServerPublicKey { get; init; } = string.Empty;
        public bool SidecarEnabled => ForwardMode == "socks5Sidecar";
        public string EffectiveTargetAddress => SidecarEnabled ? SidecarListen : TargetAddress;

        public static DnsTTSettings Parse(string content, string baseDir)
        {
            var values = ParseKeyValues(content);
            var privateKeyFile = ResolvePath(baseDir, Get(values, "serverPrivateKeyFile", "server.key"));
            var publicKeyFile = ResolvePath(baseDir, Get(values, "serverPublicKeyFile", "server.pub"));
            return new DnsTTSettings
            {
                Domain = Get(values, "domain", "t.example.com"),
                UdpListen = Get(values, "udpListen", ":5300"),
                ForwardMode = NormalizeForwardMode(Get(values, "forwardMode", "socks5Sidecar")),
                TargetAddress = Get(values, "targetAddress", "127.0.0.1:22"),
                SidecarListen = Get(values, "sidecarListen", "127.0.0.1:10808"),
                SidecarInboundType = NormalizeSidecarInboundType(Get(values, "sidecarInboundType", "mixed")),
                SidecarOutbound = NormalizeSidecarOutbound(Get(values, "sidecarOutbound", "direct")),
                SidecarAuthEnabled = IsTrue(Get(values, "sidecarAuthEnabled", "false")),
                SidecarUsername = Get(values, "sidecarUsername", "dnstt"),
                SidecarPassword = Get(values, "sidecarPassword", string.Empty),
                SidecarBinaryPath = Get(values, "sidecarBinaryPath", string.Empty),
                SidecarLogLevel = NormalizeLogLevel(Get(values, "sidecarLogLevel", "info")),
                PrivateKeyFile = privateKeyFile,
                PublicKeyFile = publicKeyFile,
                ServerPublicKey = Get(values, "serverPublicKey", string.Empty)
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
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }

            return trimmed;
        }
    }
}
