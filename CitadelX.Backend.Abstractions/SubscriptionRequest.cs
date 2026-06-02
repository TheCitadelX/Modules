namespace CitadelX.Backend.Cores;

/// <summary>
/// Everything a core module needs to render client subscription links for one server/user.
/// The Backend core owns host resolution and aggregation; the core-specific config parsing and
/// link format live inside the module (D1, MODULE_SYSTEM_SPEC §12 decoupling track), so the core
/// carries no sing-box-specific (or any core-specific) subscription knowledge.
/// </summary>
public sealed class SubscriptionRequest
{
    /// <summary>The server's materialized native config (e.g. sing-box JSON).</summary>
    public required string Config { get; init; }

    /// <summary>The user's external id, used to locate the user's credentials inside the config.</summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Per-user identity material for THIS user on THIS server: the attach-time user template
    /// (e.g. <c>{"uuid":"...","flow":"..."}</c>). Users are managed by attachment, not by editing
    /// the base config, so the module synthesizes the user into the inbound shape taken from
    /// <see cref="Config"/> using these credentials. Null when the attachment carried no template,
    /// in which case the module falls back to a user already listed in <see cref="Config"/> whose
    /// identity matches <see cref="UserId"/> (config-managed / legacy case).
    /// </summary>
    public string? UserCredentialsJson { get; init; }

    /// <summary>
    /// Backend-assigned per-server/user resources, for modules that need stable addresses, ports,
    /// interface names, or similar node-local identity. Null for legacy attachments.
    /// </summary>
    public string? ResourceAllocationJson { get; init; }

    /// <summary>Public host/domain the client should connect to.</summary>
    public required string Host { get; init; }

    /// <summary>Label used as the link fragment (<c>#name</c>) in generated URIs.</summary>
    public string Label { get; init; } = "CitadelX";
}
