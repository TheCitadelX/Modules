using CitadelX.Modules.Abstractions;

namespace CitadelX.Backend.Cores;

/// <summary>
/// Computes cached, optimistic per-core availability by matching a core's static
/// <see cref="CompatibilityDescriptor"/> against a node's reported
/// <see cref="NodeEnvironment"/>. This is only a UI filter — the node is the final
/// authority and re-validates at apply time (§8.2).
/// </summary>
public static class CompatibilityEvaluator
{
    public static CompatibilityResult Evaluate(CompatibilityDescriptor descriptor, NodeEnvironment environment)
    {
        if (descriptor.SupportedOs.Count > 0 && !descriptor.SupportedOs.Contains(environment.Os))
        {
            return CompatibilityResult.Incompatible(
                $"Requires OS {string.Join("/", descriptor.SupportedOs)}; node reports {environment.Os}.");
        }

        if (descriptor.SupportedArch.Count > 0 && !descriptor.SupportedArch.Contains(environment.Arch))
        {
            return CompatibilityResult.Incompatible(
                $"Requires arch {string.Join("/", descriptor.SupportedArch)}; node reports {environment.Arch}.");
        }

        foreach (var feature in descriptor.RequiredFeatures)
        {
            if (!HasFeature(feature, environment))
            {
                return CompatibilityResult.Incompatible($"Node is missing required feature: {feature}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(descriptor.MinOsVersion)
            && Version.TryParse(descriptor.MinOsVersion, out var minVersion)
            && TryExtractVersion(environment.OsVersion, out var nodeVersion)
            && nodeVersion < minVersion)
        {
            return CompatibilityResult.Incompatible(
                $"Requires OS version >= {descriptor.MinOsVersion}; node reports {environment.OsVersion}.");
        }

        return CompatibilityResult.Compatible();
    }

    private static bool HasFeature(RequiredFeature feature, NodeEnvironment env) => feature switch
    {
        // No dedicated NetAdmin probe yet; admin/root implies it for Phase 1.
        RequiredFeature.RootOrAdmin => env.HasAdminOrRoot,
        RequiredFeature.NetAdmin => env.HasAdminOrRoot,
        RequiredFeature.TunDevice => env.HasTunDevice,
        RequiredFeature.WireguardKernelModule => env.HasWireguardKernelModule,
        RequiredFeature.Docker => env.HasDocker,
        _ => false,
    };

    /// <summary>OsVersion is free-form (e.g. RuntimeInformation.OSDescription); pull the first dotted number out.</summary>
    private static bool TryExtractVersion(string? osVersion, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(osVersion))
        {
            return false;
        }

        foreach (var token in osVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Version.TryParse(token, out var parsed))
            {
                version = parsed;
                return true;
            }
        }

        return false;
    }
}
