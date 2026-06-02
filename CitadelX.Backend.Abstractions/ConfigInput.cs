using System.Text.Json.Nodes;

namespace CitadelX.Backend.Cores;

/// <summary>
/// The admin's config choices as captured by the Frontend. The Frontend produces this
/// (structured form values or raw text) and posts it; it never builds native config.
/// The backend module turns it into a <c>ConfigArtifact</c> via BuildConfig (added in a
/// later step). Stored on the server so the UI can reopen the form for editing.
/// </summary>
public sealed class ConfigInput
{
    public ConfigInputMode Mode { get; init; }

    /// <summary>Structured values validated against <c>ConfigContract.SchemaJson</c>. Set when <see cref="Mode"/> is Structured.</summary>
    public JsonObject? Structured { get; init; }

    /// <summary>Raw native config text. Set when <see cref="Mode"/> is Raw.</summary>
    public string? Raw { get; init; }
}

public enum ConfigInputMode
{
    Structured,
    Raw,
}
