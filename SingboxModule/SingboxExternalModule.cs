using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using Microsoft.Extensions.Options;

namespace CitadelX.SingboxModule;

public sealed class SingboxExternalModule : ICoreModule
{
    private readonly CoreRepoOptions _repos;

    public SingboxExternalModule(IOptions<CoreRepoOptions> repos)
    {
        _repos = repos.Value;
    }

    public string Id => "Singbox";
    public string Label => "Singbox";
    public string? Description => "The universal proxy platform\nAuthor: sagernet";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => null;
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "-c \"{configPath}\"",
        UseRunCommand = true
    };
    public GitHubRepo? Repo => _repos.Singbox;
    public string? NodeModuleAssemblyName => "CitadelX.SingboxNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "sing-box", "singbox" };
}
