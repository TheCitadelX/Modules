namespace CitadelX.Node.Abstractions;

public interface IServer
{
    Task Start();
    Task Stop();
    Task Restart();
    Task Reconfigure(string serverConfigJsonOrPath);
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
}

public interface INodeCoreModule
{
    string CoreId { get; }
    IReadOnlyList<string> Aliases { get; }
    INodeServer Create(ServerLaunchProfile profile);
}
