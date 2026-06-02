using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;
using System.Text.Json;

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

    /// <summary>
    /// Optional branding hint the Frontend maps to a static asset (e.g. <c>/icons/{IconKey}.svg</c>).
    /// Keeps core-specific names out of Frontend logic (D5, §12): the panel never switches on a core Id.
    /// Null means "use the generic placeholder".
    /// </summary>
    string? IconKey => null;

    // --- Universal module pipeline (MODULE_SYSTEM_SPEC §6) ---
    // Added as default interface implementations so this stays additive: existing modules
    // (including externally-compiled plugin DLLs) keep working until they opt in. Legacy
    // members above are retained for the Phase 1 migration window (§13.1) and removed later.

    /// <summary>How the node executes this core. Metadata only — never a switch in Backend/Frontend (§5).</summary>
    RuntimeKind RuntimeKind => RuntimeKind.Process;

    /// <summary>What config input the core accepts and how the Frontend should present it (§7).</summary>
    ConfigContract Config => new();

    /// <summary>Static requirements matched against a node's reported environment (§8).</summary>
    CompatibilityDescriptor Compatibility => CompatibilityDescriptor.Any;

    /// <summary>How the core is installed on a node (§9).</summary>
    InstallDescriptor Install => NoInstall.Instance;

    /// <summary>
    /// THE generator: turn the admin's <see cref="ConfigInput"/> into a native
    /// <see cref="ConfigArtifact"/> for the given node. Runs on the Backend, inside the module.
    /// Returns null when the module has not yet been migrated to the universal pipeline.
    /// </summary>
    ConfigArtifact? BuildConfig(ConfigInput input, NodeContext node) => null;

    /// <summary>
    /// Render client subscription links (e.g. <c>vless://...</c>) for one user on one server.
    /// The Backend core resolves the host and aggregates links across servers; the core-specific
    /// config parsing and URI format live here so the core stays core-agnostic (D1, §12).
    /// Returns an empty list when the core has no subscription representation.
    /// </summary>
    IReadOnlyList<string> BuildSubscriptionLinks(SubscriptionRequest request) => Array.Empty<string>();

    /// <summary>
    /// Render a typed subscription payload. URI-list output is the default legacy-compatible shape;
    /// modules can override this for native client config files or future subscription media.
    /// </summary>
    SubscriptionPayload BuildSubscription(SubscriptionRequest request)
        => SubscriptionPayload.UriList(BuildSubscriptionLinks(request));

    /// <summary>
    /// Optional module-owned per-user credential generation/normalization. Existing cores can keep
    /// passing the admin-provided template through unchanged; keypair-based cores can generate one here.
    /// </summary>
    string? BuildUserTemplate(UserTemplateRequest request) => request.UserTemplateJson;

    /// <summary>
    /// Optional hook for applying non-secret node reports (for example a generated public key)
    /// to the persisted config artifact after a command ACK. Default is no-op.
    /// </summary>
    ConfigArtifact ApplyNodeReport(ConfigArtifact artifact, JsonElement result) => artifact;
}
