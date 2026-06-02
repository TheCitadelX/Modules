using CitadelX.Modules.Abstractions;

namespace CitadelX.Backend.Cores;

/// <summary>
/// Node-relevant context passed to <see cref="ICoreModule.BuildConfig"/> so a module can tailor
/// the generated artifact to the target node. The Backend never bakes node-local secrets from this;
/// secrets/paths/allocated resources are emitted as placeholder directives and resolved on the node
/// (MODULE_SYSTEM_SPEC §7.4). This carries only what generation legitimately needs to know up front.
/// </summary>
public sealed class NodeContext
{
    /// <summary>The node the config is being generated for.</summary>
    public required Guid NodeId { get; init; }

    /// <summary>The server (core instance) the config belongs to, when known.</summary>
    public Guid? ServerId { get; init; }

    /// <summary>The node's last reported capability report, if available (§8.2). May be null.</summary>
    public NodeEnvironment? Environment { get; init; }
}
