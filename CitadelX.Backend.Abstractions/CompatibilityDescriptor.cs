using CitadelX.Modules.Abstractions;

namespace CitadelX.Backend.Cores;

/// <summary>
/// Static, self-described requirements of a core. Matched against a node's reported
/// <see cref="NodeEnvironment"/> to compute per-node availability. Empty collections mean
/// "no constraint".
/// </summary>
public sealed class CompatibilityDescriptor
{
    public IReadOnlyList<OsKind> SupportedOs { get; init; } = Array.Empty<OsKind>();

    public IReadOnlyList<CpuArch> SupportedArch { get; init; } = Array.Empty<CpuArch>();

    public IReadOnlyList<RequiredFeature> RequiredFeatures { get; init; } = Array.Empty<RequiredFeature>();

    public string? MinOsVersion { get; init; }

    /// <summary>No constraints — runs anywhere the node reports.</summary>
    public static CompatibilityDescriptor Any { get; } = new();
}
