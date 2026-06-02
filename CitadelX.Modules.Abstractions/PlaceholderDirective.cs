using System.Text.Json.Nodes;

namespace CitadelX.Modules.Abstractions;

/// <summary>Category of a node-local placeholder the node must resolve at apply time.</summary>
public enum PlaceholderKind
{
    /// <summary>A secret generated/held only on the node (e.g. a WireGuard private key).</summary>
    Secret,

    /// <summary>A node-local filesystem path.</summary>
    NodePath,

    /// <summary>A node-allocated resource (interface name, subnet, port).</summary>
    AllocatedResource,
}

/// <summary>
/// Declares a placeholder token embedded in a <see cref="ConfigArtifact"/> that the
/// Backend must NOT bake. The node resolves it locally at apply time. For secrets,
/// only safe derived values (e.g. a public key) are reported back to Backend.
/// </summary>
public sealed class PlaceholderDirective
{
    /// <summary>Token as it appears in the artifact, e.g. "${node.secret.wgPrivateKey}".</summary>
    public required string Token { get; init; }

    public required PlaceholderKind Kind { get; init; }

    /// <summary>Named generator/strategy the node uses to produce the value, e.g. "wireguard-keypair".</summary>
    public string? Generator { get; init; }

    /// <summary>Optional generator parameters.</summary>
    public JsonObject? Options { get; init; }
}
