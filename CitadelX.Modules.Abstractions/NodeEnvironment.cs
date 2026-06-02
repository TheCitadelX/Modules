namespace CitadelX.Modules.Abstractions;

/// <summary>
/// A node's environment fingerprint: what it can actually do. Produced by the
/// node (probe), reported to Backend (stored separately from installed-core
/// inventory), and consumed by a node module's compatibility probe.
/// </summary>
public sealed class NodeEnvironment
{
    public OsKind Os { get; init; } = OsKind.Unknown;

    public string? OsVersion { get; init; }

    public CpuArch Arch { get; init; } = CpuArch.Unknown;

    public bool HasAdminOrRoot { get; init; }

    public bool HasTunDevice { get; init; }

    public bool HasWireguardKernelModule { get; init; }

    public bool HasDocker { get; init; }
}
