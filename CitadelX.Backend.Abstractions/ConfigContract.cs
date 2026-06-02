using CitadelX.Modules.Abstractions;

namespace CitadelX.Backend.Cores;

/// <summary>
/// Declares what config input a core accepts and how the Frontend should present it.
/// Replaces the old <c>SupportsSimpleSetup</c> + unused <c>SimpleSetupSchema</c> pair
/// with a single contract the generic Frontend renderer and the backend generator rely on.
/// </summary>
public sealed class ConfigContract
{
    /// <summary>Schema-driven guided setup is available.</summary>
    public bool SupportsStructured { get; init; }

    /// <summary>Admin may paste native config directly.</summary>
    public bool SupportsRaw { get; init; }

    /// <summary>JSON Schema for structured input (consumed by the generic renderer).</summary>
    public string? SchemaJson { get; init; }

    /// <summary>UI hints for the renderer (sections/tabs/labels/order).</summary>
    public string? UiSchemaJson { get; init; }

    /// <summary>Default structured values.</summary>
    public string? DefaultsJson { get; init; }

    /// <summary>Native format the core consumes.</summary>
    public NativeFormat NativeFormat { get; init; } = NativeFormat.None;

    /// <summary>
    /// Optional Monaco/editor language override for native config. Use when the broad
    /// <see cref="NativeFormat"/> is "Text" but the actual syntax is known (for example TOML).
    /// </summary>
    public string? EditorLanguage { get; init; }

    public bool SupportsUsers { get; init; }

    public UserIdentityKind UserIdentity { get; init; } = UserIdentityKind.None;

    /// <summary>
    /// Sing-box-style advanced graph editor over raw/native config. Off by default;
    /// never assumed universal.
    /// </summary>
    public bool SupportsFlowEditor { get; init; }
}

/// <summary>What a "user" means for a core, so the user-management UI can adapt.</summary>
public enum UserIdentityKind
{
    None,
    Username,
    Uuid,
    WireguardPeer,
}
