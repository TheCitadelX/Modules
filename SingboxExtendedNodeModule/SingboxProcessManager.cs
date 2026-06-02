using System.Diagnostics;
using CitadelX.Modules.Abstractions;
using CitadelX.Node.Abstractions;

namespace CitadelX.SingboxExtendedNodeModule;

public sealed class SingboxProcessManager
{
    private readonly object _sync = new();
    private Process? _process;
    private bool _shouldBeRunning;
    private string? _binaryPath;
    private string? _arguments;
    private bool _useRunCommand = true;
    private string? _workingDirectory;
    private string? _configPath;
    private DateTimeOffset? _startedAt;
    private int? _lastExitCode;
    private RollingServerLog? _log;

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

    public bool ShouldBeRunning
    {
        get
        {
            lock (_sync)
            {
                return _shouldBeRunning;
            }
        }
    }

    public string? ConfigPath
    {
        get
        {
            lock (_sync)
            {
                return _configPath;
            }
        }
    }

    public DateTimeOffset? StartedAt
    {
        get
        {
            lock (_sync)
            {
                return IsRunning ? _startedAt : null;
            }
        }
    }

    public void ApplyProfile(ServerLaunchProfile profile)
    {
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(profile.BinaryPath))
            {
                _binaryPath = profile.BinaryPath;
            }

            if (!string.IsNullOrWhiteSpace(profile.Arguments))
            {
                _arguments = profile.Arguments;
            }

            if (profile.UseRunCommand.HasValue)
            {
                _useRunCommand = profile.UseRunCommand.Value;
            }

            if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
            {
                _workingDirectory = profile.WorkingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(profile.ConfigPath))
            {
                _configPath = profile.ConfigPath;
            }

            var logPath = !string.IsNullOrWhiteSpace(profile.LogPath)
                ? profile.LogPath
                : ResolveDefaultLogPath(profile.ServerId);
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                _log = new RollingServerLog(logPath);
            }
        }
    }

    public void SetConfigPath(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(configPath);
        lock (_sync)
        {
            _configPath = fullPath;
        }
    }

    public Task Start()
    {
        lock (_sync)
        {
            _shouldBeRunning = true;
            if (_process is not null && !_process.HasExited)
            {
                return Task.CompletedTask;
            }

            var binaryPath = RequireBinaryPath();
            var args = BuildArguments();
            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(_workingDirectory))
            {
                startInfo.WorkingDirectory = _workingDirectory;
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.Exited += OnProcessExited;
            process.OutputDataReceived += (_, eventArgs) => _log?.Append("stdout", eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => _log?.Append("stderr", eventArgs.Data);

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start sing-box process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _startedAt = DateTimeOffset.UtcNow;
            _lastExitCode = null;
            _log?.Append("system", $"started pid={process.Id}");
            return Task.CompletedTask;
        }
    }

    public async Task Stop()
    {
        Process? process;
        lock (_sync)
        {
            _shouldBeRunning = false;
            process = _process;
        }

        if (process is null || process.HasExited)
        {
            return;
        }

        try
        {
            var timeout = TimeSpan.FromSeconds(10);
            if (process.CloseMainWindow())
            {
                if (await WaitForExitAsync(process, timeout))
                {
                    return;
                }
            }

            process.Kill(entireProcessTree: true);
            await WaitForExitAsync(process, timeout);
        }
        finally
        {
            lock (_sync)
            {
                _log?.Append("system", "stopped");
                _process?.Dispose();
                _process = null;
                _startedAt = null;
                _lastExitCode = null;
            }
        }
    }

    public async Task Restart()
    {
        await Stop();
        await Start();
    }

    private string RequireBinaryPath()
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(_binaryPath))
            {
                throw new InvalidOperationException("BinaryPath is not configured.");
            }

            return _binaryPath;
        }
    }

    private string BuildArguments()
    {
        var configPath = RequireConfigPath();
        var args = string.IsNullOrWhiteSpace(_arguments)
            ? $"-c \"{configPath}\""
            : _arguments.Replace("{configPath}", configPath, StringComparison.OrdinalIgnoreCase).Trim();

        if (_useRunCommand && !StartsWithRun(args))
        {
            args = $"run {args}";
        }

        return args;
    }

    private string RequireConfigPath()
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(_configPath))
            {
                throw new InvalidOperationException("ConfigPath is not configured.");
            }

            return Path.GetFullPath(_configPath);
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
            var exitCode = TryGetExitCode(process);
            _lastExitCode = exitCode;
            _log?.Append("system", exitCode.HasValue ? $"exited code={exitCode.Value}" : "exited");
            _process?.Dispose();
            _process = null;
            _startedAt = null;
        }
    }

    public Task<ServerLogChunk> ReadLogsAsync(ServerLogQuery query)
    {
        lock (_sync)
        {
            return _log?.ReadAsync(query) ?? Task.FromResult(new ServerLogChunk());
        }
    }

    public ServerRuntimeState GetRuntimeState()
    {
        lock (_sync)
        {
            return new ServerRuntimeState
            {
                IsRunning = _process is not null && !_process.HasExited,
                StartedAt = _process is not null && !_process.HasExited ? _startedAt : null,
                Health = _process is not null && !_process.HasExited
                    ? ServerRuntimeHealth.Running
                    : _lastExitCode is > 0
                        ? ServerRuntimeHealth.Failed
                        : ServerRuntimeHealth.Stopped,
                StatusMessage = _lastExitCode is > 0 ? $"Process exited with code {_lastExitCode.Value}." : null
            };
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

    private static bool StartsWithRun(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return false;
        }

        var firstToken = args.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        return string.Equals(firstToken, "run", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDefaultLogPath(string? serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return null;
        }

        var safeId = string.Concat(serverId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
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
}
