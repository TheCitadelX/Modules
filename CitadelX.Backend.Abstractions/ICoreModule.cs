using CitadelX.Backend.Options;

namespace CitadelX.Backend.Cores;

public interface ICoreModule
{
    string Id { get; }
    string Label { get; }
    string? Description { get; }
    bool Ready { get; }
    bool SupportsAutoInstall { get; }
    bool SupportsSimpleSetup { get; }
    CoreConfigSchema? SimpleSetupSchema { get; }
    CoreLaunchProfile? LaunchProfile { get; }
    GitHubRepo? Repo { get; }
    string? NodeModuleAssemblyName { get; }
    IReadOnlyList<string> Aliases { get; }
}
