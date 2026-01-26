using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;

namespace CitadelX.SingboxExtendedModule;

public sealed class SingboxExtendedExternalModule : ICoreModule
{
    private readonly CoreRepoOptions _repos;

    public SingboxExtendedExternalModule(CoreRepoOptions repos)
    {
        _repos = repos;
    }

    public string Id => "SingboxExtended";
    public string Label => "Singbox Extended";
    public string? Description => "Singbox with many new features\nAuthor: sagernet, shtorm-7";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => false;
    public CoreConfigSchema? SimpleSetupSchema => null;
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "-c \"{configPath}\"",
        UseRunCommand = true
    };
    public GitHubRepo? Repo => _repos.SingboxExtended;
    public string? NodeModuleAssemblyName => "CitadelX.SingboxExtendedNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "sing-box-extended", "singbox-extended" };
}
