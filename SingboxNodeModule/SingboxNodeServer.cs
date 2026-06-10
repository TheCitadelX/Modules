using System.Text.Json.Nodes;
using CitadelX.Modules.Abstractions;
using CitadelX.Node.Abstractions;
using Microsoft.Extensions.Logging;

namespace CitadelX.SingboxNodeModule;

public sealed class SingboxNodeServer : INodeServer
{
    private readonly SingboxProcessManager _processManager;
    private readonly SingboxConfigPatcher _patcher;
    private readonly AtomicFileWriter _fileWriter;
    private readonly DisabledUserStore _disabledUserStore;
    private readonly ILogger<SingboxNodeServer> _logger;
    private readonly SingboxTelemetryCollector _telemetry;
    private ServerLaunchProfile _profile;

    public SingboxNodeServer(ServerLaunchProfile profile, AtomicFileWriter fileWriter, ILogger<SingboxNodeServer> logger)
    {
        _profile = profile;
        _fileWriter = fileWriter;
        _logger = logger;
        _processManager = new SingboxProcessManager();
        _processManager.ApplyProfile(profile);
        _patcher = new SingboxConfigPatcher();
        _disabledUserStore = new DisabledUserStore(() => _processManager.ConfigPath, _fileWriter);
        _telemetry = new SingboxTelemetryCollector(profile.ServerId);
    }

    public bool IsRunning => _processManager.IsRunning;

    public Task<ServerLogChunk> ReadLogsAsync(ServerLogQuery query)
    {
        return _processManager.ReadLogsAsync(query);
    }

    public Task ClearLogsAsync()
    {
        return _processManager.ClearLogsAsync();
    }

    public ServerRuntimeState GetRuntimeState()
    {
        return _processManager.GetRuntimeState();
    }

    public void ApplyProfile(ServerLaunchProfile profile)
    {
        _profile = profile;
        _processManager.ApplyProfile(profile);
        _telemetry.SetServerId(profile.ServerId);
    }

    public async Task Start()
    {
        EnsureTelemetryConfiguration();
        await _processManager.Start();
    }

    public Task Stop()
    {
        return _processManager.Stop();
    }

    public async Task Restart()
    {
        EnsureTelemetryConfiguration();
        await _processManager.Restart();
    }

    public IReadOnlyList<ServerUserRuntimeSnapshot> GetUserRuntimeSnapshots()
    {
        return _telemetry.Collect(_processManager.IsRunning);
    }

    public async Task Apply(ConfigArtifact artifact)
    {
        if (artifact is null)
        {
            throw new ArgumentNullException(nameof(artifact));
        }

        if (artifact is not FileArtifact file)
        {
            throw new NotSupportedException(
                $"Sing-box runs as a process and only supports file artifacts; received '{artifact.GetType().Name}'.");
        }

        if (string.IsNullOrWhiteSpace(file.Content))
        {
            throw new ArgumentException("Config artifact content cannot be empty.", nameof(artifact));
        }

        var targetPath = GetConfigPath();
        var root = _patcher.LoadJson(file.Content);
        var telemetryConfiguration = ConfigureTelemetry(root, reuseExistingListenAddress: false);
        _fileWriter.WriteAllTextAtomic(targetPath, _patcher.Serialize(root));
        _telemetry.UpdateConfiguration(telemetryConfiguration);
        _processManager.SetConfigPath(targetPath);
        if (!_processManager.IsRunning)
        {
            await _processManager.Start();
            _logger.LogInformation("Sing-box config applied and process started. Path={ConfigPath}", targetPath);
            return;
        }

        await _processManager.Restart();
        _logger.LogInformation("Sing-box config applied and process restarted. Path={ConfigPath}", targetPath);
    }

    public async Task AddUser(UserEntity user, JsonObject? userTemplate = null)
    {
        await PatchConfig(root => _patcher.AddUser(root, user, null, null, userTemplate));
        _disabledUserStore.Remove(user.Id);
        _logger.LogInformation("User {UserId} added.", user.Id);
    }

    public async Task EditUser(UserEntity user, JsonObject? userTemplate = null)
    {
        await PatchConfig(root => _patcher.EditUser(root, user, null, null, userTemplate));
        _logger.LogInformation("User {UserId} edited.", user.Id);
    }

    public async Task RemoveUser(string userId)
    {
        await PatchConfig(root => _patcher.RemoveUser(root, userId, null, null));
        _disabledUserStore.Remove(userId);
        _logger.LogInformation("User {UserId} removed.", userId);
    }

    public async Task DisableUser(string userId)
    {
        await PatchConfig(root =>
        {
            var removed = _patcher.RemoveUser(root, userId, null, null);
            if (removed is not null)
            {
                _disabledUserStore.Save(userId, removed);
            }
        });

        _logger.LogInformation("User {UserId} disabled.", userId);
    }

    public async Task EnableUser(string userId)
    {
        var stored = _disabledUserStore.TryTake(userId);
        if (stored is null)
        {
            throw new InvalidOperationException($"User '{userId}' is not in the disabled store.");
        }

        await PatchConfig(root => _patcher.AddUser(root, new UserEntity { Id = userId }, null, null, stored));
        _logger.LogInformation("User {UserId} enabled.", userId);
    }

    public async Task SyncUsers(IReadOnlyCollection<string> allowedUserIds)
    {
        var configPath = GetConfigPath();
        var root = _patcher.LoadJson(configPath);
        var removedIds = _patcher.RemoveUsersNotIn(root, allowedUserIds, null, null);
        if (removedIds.Count == 0)
        {
            return;
        }

        foreach (var removedId in removedIds)
        {
            _disabledUserStore.Remove(removedId);
        }

        var telemetryConfiguration = ConfigureTelemetry(root);
        _fileWriter.WriteAllTextAtomic(configPath, _patcher.Serialize(root));
        _telemetry.UpdateConfiguration(telemetryConfiguration);
        if (_processManager.IsRunning)
        {
            await _processManager.Restart();
        }
        if (removedIds.Count > 0)
        {
            _logger.LogInformation("User sync removed {Count} user(s).", removedIds.Count);
        }
    }

    private async Task PatchConfig(Action<JsonNode> patch)
    {
        var configPath = GetConfigPath();
        var root = _patcher.LoadJson(configPath);
        patch(root);
        var telemetryConfiguration = ConfigureTelemetry(root);
        _fileWriter.WriteAllTextAtomic(configPath, _patcher.Serialize(root));
        _telemetry.UpdateConfiguration(telemetryConfiguration);
        if (_processManager.IsRunning)
        {
            await _processManager.Restart();
        }
    }

    private void EnsureTelemetryConfiguration()
    {
        var configPath = GetConfigPath();
        var root = _patcher.LoadJson(configPath);
        var telemetryConfiguration = ConfigureTelemetry(root);
        if (telemetryConfiguration.Changed)
        {
            _fileWriter.WriteAllTextAtomic(configPath, _patcher.Serialize(root));
        }

        _telemetry.UpdateConfiguration(telemetryConfiguration);
    }

    private SingboxV2RayApiConfiguration ConfigureTelemetry(
        JsonNode root,
        bool reuseExistingListenAddress = true)
    {
        if (string.IsNullOrWhiteSpace(_profile.ServerId))
        {
            throw new InvalidOperationException("ServerId is required to configure sing-box telemetry.");
        }

        return SingboxV2RayApiConfigurator.Configure(
            root,
            _profile.ServerId,
            _telemetry.ListenAddress,
            reuseExistingListenAddress);
    }

    private string GetConfigPath()
    {
        var path = _processManager.ConfigPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "singbox.config.json");
        _processManager.SetConfigPath(fallback);
        return fallback;
    }
}
