using CitadelX.Modules.Abstractions;

namespace CitadelX.Node.Abstractions;

public interface IServer
{
    Task Start();
    Task Stop();
    Task Restart();

    /// <summary>
    /// Materialize and apply a config artifact produced by the backend module.
    /// The node owns where/how the artifact lands (file, API operations, ...);
    /// node-local placeholders are resolved here at apply time.
    /// </summary>
    Task Apply(ConfigArtifact artifact);
}

public interface IManagedServer
{
    Task AddUser(UserEntity user, System.Text.Json.Nodes.JsonObject? userTemplate = null);
    Task EditUser(UserEntity user, System.Text.Json.Nodes.JsonObject? userTemplate = null);
    Task RemoveUser(string userId);
    Task DisableUser(string userId);
    Task EnableUser(string userId);
    Task SyncUsers(IReadOnlyCollection<string> allowedUserIds);
}

public interface INodeServer : IServer, IManagedServer
{
    bool IsRunning { get; }
    void ApplyProfile(ServerLaunchProfile profile);

    Task<ServerLogChunk> ReadLogsAsync(ServerLogQuery query)
        => Task.FromResult(new ServerLogChunk());

    Task ClearLogsAsync()
        => Task.CompletedTask;

    ServerRuntimeState GetRuntimeState()
        => new()
        {
            IsRunning = IsRunning,
            Health = IsRunning ? ServerRuntimeHealth.Running : ServerRuntimeHealth.Stopped
        };

    IReadOnlyList<ServerUserRuntimeSnapshot> GetUserRuntimeSnapshots()
        => Array.Empty<ServerUserRuntimeSnapshot>();
}

/// <summary>
/// Optional runtime hook for process modules that need to return non-secret apply results
/// to the backend after a successful <c>server.reconfigure</c>.
/// </summary>
public interface INodeApplyReportProvider
{
    System.Text.Json.Nodes.JsonObject? GetLastApplyReport();
}

public interface INodeCoreModule
{
    string CoreId { get; }
    IReadOnlyList<string> Aliases { get; }
    INodeServer Create(ServerLaunchProfile profile);
}
