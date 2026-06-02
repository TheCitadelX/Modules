namespace CitadelX.Modules.Abstractions;

/// <summary>Operating system family of a node.</summary>
public enum OsKind
{
    Unknown,
    Windows,
    Linux,
    MacOS,
}

/// <summary>CPU architecture of a node.</summary>
public enum CpuArch
{
    Unknown,
    X64,
    Arm64,
    X86,
    Arm,
}

/// <summary>
/// A capability a core may require from a node. Matched against a node's
/// reported <see cref="NodeEnvironment"/> to decide availability.
/// </summary>
public enum RequiredFeature
{
    RootOrAdmin,
    NetAdmin,
    TunDevice,
    WireguardKernelModule,
    Docker,
}
