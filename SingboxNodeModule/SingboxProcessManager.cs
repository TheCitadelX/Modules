using System.Diagnostics;
using CitadelX.Node.Abstractions;

namespace CitadelX.SingboxNodeModule;

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

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start sing-box process.");
            }

            _process = process;
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
                _process?.Dispose();
                _process = null;
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
            _process?.Dispose();
            _process = null;
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
}
