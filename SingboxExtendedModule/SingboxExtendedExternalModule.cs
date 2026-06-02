using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.SingboxExtendedModule;

public sealed class SingboxExtendedExternalModule : ICoreModule
{
    // The module owns its own default upstream repo; the panel-wide config can override it by core
    // Id (§12). This fork lives at shtorm-7/sing-box-extended — a name no abstraction needs to know.
    private static readonly GitHubRepo DefaultRepo = new() { Owner = "shtorm-7", Repo = "sing-box-extended" };

    private readonly CoreRepoOptions _repos;

    public SingboxExtendedExternalModule(CoreRepoOptions repos)
    {
        _repos = repos;
    }

    private GitHubRepo ResolvedRepo => _repos.Resolve(Id) ?? DefaultRepo;

    public string Id => "SingboxExtended";
    public string Label => "Singbox Extended";
    public string? Description => "Singbox with many new features\nAuthor: sagernet, shtorm-7";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => SingboxExtendedSimpleSetupSchema.Schema;
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "-c \"{configPath}\"",
        UseRunCommand = true
    };
    public GitHubRepo? Repo => ResolvedRepo;
    public string? NodeModuleAssemblyName => "CitadelX.SingboxExtendedNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "sing-box-extended", "singbox-extended" };
    public string? IconKey => "singbox";

    // --- Universal module pipeline ---
    // SingboxExtended keeps its own minimal structured generator so "next-next-create" produces
    // a runnable direct-proxy config without depending on the base Singbox module assembly.

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
        SupportsFlowEditor = true,
        SchemaJson = SingboxExtendedSimpleSetupSchema.Schema.SchemaJson,
        DefaultsJson = SingboxExtendedSimpleSetupSchema.Schema.DefaultsJson,
    };

    public ConfigArtifact BuildConfig(ConfigInput input, NodeContext node) => new FileArtifact
    {
        FileName = "config.json",
        Content = input.Mode == ConfigInputMode.Structured
            ? SingboxExtendedConfigBuilder.Build(input.Structured ?? new System.Text.Json.Nodes.JsonObject())
            : input.Raw ?? SingboxExtendedConfigBuilder.Build(new System.Text.Json.Nodes.JsonObject()),
        Format = NativeFormat.Json,
    };

    // SingboxExtended is an independent fork and owns its own link builder (D1 / §12 decoupling) —
    // it must not depend on the base Singbox module, so the two can diverge freely.
    public IReadOnlyList<string> BuildSubscriptionLinks(SubscriptionRequest request)
        => SingboxExtendedSubscriptionBuilder.Build(request.Config, request.UserId, request.Host, request.Label, request.UserCredentialsJson);

    public SubscriptionPayload BuildSubscription(SubscriptionRequest request)
    {
        var links = BuildSubscriptionLinks(request);
        var clientConfig = SingboxExtendedSubscriptionBuilder.BuildClientConfig(
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
