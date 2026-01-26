using System.Text.Json.Nodes;
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

    public SingboxNodeServer(ServerLaunchProfile profile, AtomicFileWriter fileWriter, ILogger<SingboxNodeServer> logger)
    {
        _fileWriter = fileWriter;
        _logger = logger;
        _processManager = new SingboxProcessManager();
        _processManager.ApplyProfile(profile);
        _patcher = new SingboxConfigPatcher();
        _disabledUserStore = new DisabledUserStore(() => _processManager.ConfigPath, _fileWriter);
    }

    public bool IsRunning => _processManager.IsRunning;

    public void ApplyProfile(ServerLaunchProfile profile)
    {
        _processManager.ApplyProfile(profile);
    }

    public Task Start()
    {
        return _processManager.Start();
    }

    public Task Stop()
    {
        return _processManager.Stop();
    }

    public Task Restart()
    {
        return _processManager.Restart();
    }

    public async Task Reconfigure(string serverConfigJsonOrPath)
    {
        if (string.IsNullOrWhiteSpace(serverConfigJsonOrPath))
        {
            throw new ArgumentException("Config payload cannot be empty.", nameof(serverConfigJsonOrPath));
        }

        if (!LooksLikeJson(serverConfigJsonOrPath))
        {
            var configPath = Path.GetFullPath(serverConfigJsonOrPath);
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Config file does not exist.", configPath);
            }

            var currentPath = _processManager.ConfigPath;
            _processManager.SetConfigPath(configPath);
            var pathChanged = string.IsNullOrWhiteSpace(currentPath)
                              || !string.Equals(Path.GetFullPath(currentPath), configPath, StringComparison.OrdinalIgnoreCase);

            if (_processManager.IsRunning && pathChanged)
            {
                await _processManager.Restart();
                _logger.LogInformation("Sing-box config path updated and process restarted. Path={ConfigPath}", configPath);
                return;
            }

            if (!_processManager.IsRunning)
            {
                await _processManager.Start();
                _logger.LogInformation("Sing-box config path set to {ConfigPath} and process started.", configPath);
                return;
            }

            _logger.LogInformation("Sing-box config path set to {ConfigPath}.", configPath);
            return;
        }

        var targetPath = GetConfigPath();
        var normalized = _patcher.Normalize(serverConfigJsonOrPath);
        _fileWriter.WriteAllTextAtomic(targetPath, normalized);
        _processManager.SetConfigPath(targetPath);
        if (!_processManager.IsRunning)
        {
            await _processManager.Start();
            _logger.LogInformation("Sing-box config updated and process started.");
            return;
        }

        _logger.LogInformation("Sing-box config updated.");
    }

    public Task AddUser(UserEntity user, JsonObject? userTemplate = null)
    {
        PatchConfig(root => _patcher.AddUser(root, user, null, null, userTemplate));
        _disabledUserStore.Remove(user.Id);
        _logger.LogInformation("User {UserId} added.", user.Id);
        return Task.CompletedTask;
    }

    public Task EditUser(UserEntity user, JsonObject? userTemplate = null)
    {
        PatchConfig(root => _patcher.EditUser(root, user, null, null, userTemplate));
        _logger.LogInformation("User {UserId} edited.", user.Id);
        return Task.CompletedTask;
    }

    public Task RemoveUser(string userId)
    {
        PatchConfig(root => _patcher.RemoveUser(root, userId, null, null));
        _disabledUserStore.Remove(userId);
        _logger.LogInformation("User {UserId} removed.", userId);
        return Task.CompletedTask;
    }

    public Task DisableUser(string userId)
    {
        PatchConfig(root =>
        {
            var removed = _patcher.RemoveUser(root, userId, null, null);
            if (removed is not null)
            {
                _disabledUserStore.Save(userId, removed);
            }
        });

        _logger.LogInformation("User {UserId} disabled.", userId);
        return Task.CompletedTask;
    }

    public Task EnableUser(string userId)
    {
        var stored = _disabledUserStore.TryTake(userId);
        if (stored is null)
        {
            throw new InvalidOperationException($"User '{userId}' is not in the disabled store.");
        }

        PatchConfig(root => _patcher.AddUser(root, new UserEntity { Id = userId }, null, null, stored));
        _logger.LogInformation("User {UserId} enabled.", userId);
        return Task.CompletedTask;
    }

    public Task SyncUsers(IReadOnlyCollection<string> allowedUserIds)
    {
        var configPath = GetConfigPath();
        var root = _patcher.LoadJson(configPath);
        var removedIds = _patcher.RemoveUsersNotIn(root, allowedUserIds, null, null);
        if (removedIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var removedId in removedIds)
        {
            _disabledUserStore.Remove(removedId);
        }

        var serialized = _patcher.Serialize(root);
        _fileWriter.WriteAllTextAtomic(configPath, serialized);
        if (removedIds.Count > 0)
        {
            _logger.LogInformation("User sync removed {Count} user(s).", removedIds.Count);
        }

        return Task.CompletedTask;
    }

    private void PatchConfig(Action<JsonNode> patch)
    {
        var configPath = GetConfigPath();
        var root = _patcher.LoadJson(configPath);
        patch(root);
        var serialized = _patcher.Serialize(root);
        _fileWriter.WriteAllTextAtomic(configPath, serialized);
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

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }
}
