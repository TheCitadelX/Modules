using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CitadelX.Modules.Abstractions;

/// <summary>Native config representation produced by a backend module.</summary>
public enum NativeFormat
{
    None,
    Json,
    Ini,
    Text,
}

/// <summary>
/// The transport between a backend module (which generates it) and a node module
/// (which applies it). This is a versioned, discriminated DTO because it is
/// serialized into the command payload and a far-away node may apply it later.
///
/// The discriminator is the JSON property "kind" (file | operationSet | composite).
/// The Backend never bakes node-local values here; those are emitted as
/// <see cref="Placeholders"/> and resolved on the node at apply time.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(FileArtifact), "file")]
[JsonDerivedType(typeof(OperationSet), "operationSet")]
[JsonDerivedType(typeof(CompositeArtifact), "composite")]
public abstract class ConfigArtifact
{
    /// <summary>Bumped when the DTO shape changes so old pending commands stay readable.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Node-local placeholders this artifact contains, declared for the node resolver.</summary>
    public IReadOnlyList<PlaceholderDirective> Placeholders { get; init; } = Array.Empty<PlaceholderDirective>();
}

/// <summary>A native config file the node writes (sing-box JSON, wg.conf, ipsec.conf, ...).</summary>
public sealed class FileArtifact : ConfigArtifact
{
    public required string FileName { get; init; }

    /// <summary>File content. May contain placeholder tokens (see <see cref="ConfigArtifact.Placeholders"/>).</summary>
    public required string Content { get; init; }

    public NativeFormat Format { get; init; } = NativeFormat.Text;
}

/// <summary>An ordered set of API operations for cores with no local config file (REST/gRPC).</summary>
public sealed class OperationSet : ConfigArtifact
{
    /// <summary>Operations must be idempotent: re-applying converges to the same state.</summary>
    public IReadOnlyList<ConfigOperation> Operations { get; init; } = Array.Empty<ConfigOperation>();
}

/// <summary>A single API operation within an <see cref="OperationSet"/>.</summary>
public sealed class ConfigOperation
{
    /// <summary>Module-defined verb, e.g. "putConfig", "upsertUser", "removeUser".</summary>
    public required string Op { get; init; }

    /// <summary>Optional target resource/path the verb applies to.</summary>
    public string? Target { get; init; }

    /// <summary>Optional operation payload.</summary>
    public JsonObject? Payload { get; init; }
}

/// <summary>Both a base file and post-apply operations (e.g. write config, then call an API).</summary>
public sealed class CompositeArtifact : ConfigArtifact
{
    public required FileArtifact File { get; init; }

    public OperationSet Operations { get; init; } = new();
}
