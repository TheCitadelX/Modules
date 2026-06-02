using System.Text.Json.Nodes;
using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.SingboxModule;

public sealed class SingboxExternalModule : ICoreModule
{
    // The module owns its own default upstream repo; the panel-wide config can override it by core
    // Id (§12). Nothing outside this module needs to know sing-box lives at SagerNet/sing-box.
    private static readonly GitHubRepo DefaultRepo = new() { Owner = "SagerNet", Repo = "sing-box" };

    private readonly CoreRepoOptions _repos;

    public SingboxExternalModule(CoreRepoOptions repos)
    {
        _repos = repos;
    }

    private GitHubRepo ResolvedRepo => _repos.Resolve(Id) ?? DefaultRepo;

    public string Id => "Singbox";
    public string Label => "Singbox";
    public string? Description => "The universal proxy platform\nAuthor: sagernet";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => SingboxSimpleSetupSchema.Schema;
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "-c \"{configPath}\"",
        UseRunCommand = true
    };
    public GitHubRepo? Repo => ResolvedRepo;
    public string? NodeModuleAssemblyName => "CitadelX.SingboxNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "sing-box", "singbox" };
    public string? IconKey => "singbox";

    // --- Universal module pipeline (reference Process + FileArtifact implementation, §12) ---

    public RuntimeKind RuntimeKind => RuntimeKind.Process;

    // Install metadata: a GitHub release whose archive contains the "sing-box" binary. The node
    // reads the binary name from the install command, so it carries no core-specific names (D4).
    public InstallDescriptor Install => new GitHubReleaseInstall
    {
        Repo = ResolvedRepo,
        AssetRules = new AssetMatchRules { BinaryName = "sing-box" }
    };

    public ConfigContract Config => new()
    {
        SupportsStructured = true,
        SupportsRaw = true,
        NativeFormat = NativeFormat.Json,
        SupportsUsers = true,
        // sing-box is the core the advanced graph/flow editor was built for.
        SupportsFlowEditor = true,
    };

    public ConfigArtifact BuildConfig(ConfigInput input, NodeContext node)
    {
        var content = input.Mode == ConfigInputMode.Raw
            ? input.Raw ?? string.Empty
            : SingboxConfigBuilder.Build(input.Structured ?? new JsonObject());

        return new FileArtifact
        {
            FileName = "config.json",
            Content = content,
            Format = NativeFormat.Json,
        };
    }

    public IReadOnlyList<string> BuildSubscriptionLinks(SubscriptionRequest request)
        => SingboxSubscriptionBuilder.Build(request.Config, request.UserId, request.Host, request.Label, request.UserCredentialsJson);

    public SubscriptionPayload BuildSubscription(SubscriptionRequest request)
    {
        var links = BuildSubscriptionLinks(request);
        var clientConfig = SingboxSubscriptionBuilder.BuildClientConfig(
            request.Config,
            request.UserId,
            request.Host,
            request.Label,
            request.UserCredentialsJson);

        if (!string.IsNullOrWhiteSpace(clientConfig))
        {
            return SubscriptionPayload.Combined(
                links,
                $"{request.UserId}.sing-box.json",
                clientConfig,
                "application/json");
        }

        if (links.Count > 0)
        {
            return SubscriptionPayload.UriList(links);
        }

        return SubscriptionPayload.UriList(Array.Empty<string>());
    }
}
