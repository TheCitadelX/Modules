using CitadelX.Node.Abstractions;

namespace CitadelX.TrustTunnelNodeModule;

internal static class TrustTunnelConfigPaths
{
    public static string ResolveManagedDirectory(ServerLaunchProfile profile, string baseDirectory)
    {
        var safeId = string.IsNullOrWhiteSpace(profile.ServerId)
            ? "trusttunnel"
            : string.Concat(profile.ServerId.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

        return Path.Combine(baseDirectory, "data", "trusttunnel", safeId);
    }

    public static string ResolveExistingOrManagedDirectory(ServerLaunchProfile profile, string baseDirectory)
    {
        var managed = ResolveManagedDirectory(profile, baseDirectory);
        if (ContainsRequiredFiles(managed))
        {
            return managed;
        }

        var legacy = ResolveLegacyDirectory(profile.ConfigPath);
        return legacy is not null && ContainsRequiredFiles(legacy) ? legacy : managed;
    }

    private static string? ResolveLegacyDirectory(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var full = Path.GetFullPath(configuredPath);
        return Path.HasExtension(full) ? Path.GetDirectoryName(full) ?? full : full;
    }

    private static bool ContainsRequiredFiles(string directory)
        => File.Exists(Path.Combine(directory, "vpn.toml"))
           && File.Exists(Path.Combine(directory, "hosts.toml"));
}
