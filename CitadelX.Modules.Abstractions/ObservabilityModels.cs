namespace CitadelX.Modules.Abstractions;

public sealed class ServerLogQuery
{
    public long? Since { get; init; }
    public int Limit { get; init; } = 200;
    public string? Stream { get; init; } = "all";
}

public sealed class ServerLogLine
{
    public long Offset { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public required string Stream { get; init; }
    public required string Text { get; init; }
}

public sealed class ServerLogChunk
{
    public IReadOnlyList<ServerLogLine> Lines { get; init; } = Array.Empty<ServerLogLine>();
    public long? NextOffset { get; init; }
    public bool Truncated { get; init; }
}

public enum ServerRuntimeHealth
{
    Unknown,
    Starting,
    Running,
    Degraded,
    Stopped,
    Failed,
}

public sealed class ServerRuntimeState
{
    public bool IsRunning { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public ServerRuntimeHealth Health { get; init; } = ServerRuntimeHealth.Unknown;
    public string? StatusMessage { get; init; }
}

public sealed class ServerRuntimeSnapshot
{
    public required string ServerId { get; init; }
    public bool IsRunning { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public ServerRuntimeHealth Health { get; init; } = ServerRuntimeHealth.Unknown;
    public string? StatusMessage { get; init; }
    public DateTimeOffset ReportedAt { get; init; }
}

public enum ServerUserTelemetryHealth
{
    Unknown,
    Online,
    Idle,
    Offline,
    Unavailable,
    Failed,
}

public sealed class ServerUserRuntimeSnapshot
{
    public required string ServerId { get; init; }

    /// <summary>
    /// User id as known by the node module. For current user commands this is the backend user's ExternalId.
    /// </summary>
    public required string UserId { get; init; }

    public bool IsOnline { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
    public long? RxBytes { get; init; }
    public long? TxBytes { get; init; }
    public long? TrafficBytes { get; init; }
    public ServerUserTelemetryHealth Health { get; init; } = ServerUserTelemetryHealth.Unknown;
    public string? StatusMessage { get; init; }
    public DateTimeOffset ReportedAt { get; init; }
}
