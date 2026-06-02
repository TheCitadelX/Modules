using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.Backend.Cores;

/// <summary>
/// How a core is installed on a node. Data-driven so the shared installer orchestrates
/// while a node-side strategy (selected from this descriptor) does the extract/locate/place.
///
/// Note: this currently lives in Backend.Abstractions because only the backend module
/// declares it. When node-side install strategies are implemented (Phase 2+), it may move
/// to the shared assembly.
/// </summary>
public abstract class InstallDescriptor
{
}

/// <summary>Nothing to install (native in-process cores, or connect-to-existing remote cores).</summary>
public sealed class NoInstall : InstallDescriptor
{
    public static NoInstall Instance { get; } = new();
}

/// <summary>Download a binary from a GitHub release, selecting the asset by OS/arch.</summary>
public sealed class GitHubReleaseInstall : InstallDescriptor
{
    public required GitHubRepo Repo { get; init; }

    public AssetMatchRules AssetRules { get; init; } = new();
}

/// <summary>Install via an OS package manager.</summary>
public sealed class SystemPackageInstall : InstallDescriptor
{
    /// <summary>Package name per OS family.</summary>
    public IReadOnlyDictionary<OsKind, string> PackageNames { get; init; } =
        new Dictionary<OsKind, string>();

    /// <summary>Binary used to verify the package is installed, e.g. "wg-quick".</summary>
    public string? BinaryName { get; init; }
}

/// <summary>Pull a container image.</summary>
public sealed class ContainerImageInstall : InstallDescriptor
{
    public required string Image { get; init; }
}

/// <summary>Rules for picking and locating a binary inside a release asset.</summary>
public sealed class AssetMatchRules
{
    public IReadOnlyList<string> ArchiveExtensions { get; init; } = new[] { ".zip", ".tar.gz" };

    /// <summary>Binary file name to locate inside the archive (e.g. "sing-box").</summary>
    public string? BinaryName { get; init; }

    /// <summary>Optional asset name pattern to match the right OS/arch asset.</summary>
    public string? NamePattern { get; init; }
}
