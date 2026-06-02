namespace CitadelX.Modules.Abstractions;

/// <summary>
/// How a core is executed on a node. This is metadata/capability only; it never
/// becomes a switch in Backend or Frontend. Every kind implements the same
/// behavioral contract (start/stop/restart/apply + user operations).
/// </summary>
public enum RuntimeKind
{
    /// <summary>Node spawns and owns a child process (e.g. sing-box).</summary>
    Process,

    /// <summary>Node writes config and controls an OS service (systemd, wg-quick, pppd).</summary>
    SystemService,

    /// <summary>Node runs/stops a container.</summary>
    Container,

    /// <summary>Node talks to an external/sidecar daemon over HTTP REST or gRPC.</summary>
    RemoteClient,

    /// <summary>Server runs in-process inside the Node agent (e.g. a C# Socks5 server).</summary>
    InProcessNative,
}
