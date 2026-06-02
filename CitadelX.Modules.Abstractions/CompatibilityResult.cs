namespace CitadelX.Modules.Abstractions;

/// <summary>
/// Result of evaluating whether a core can run on a node. The node is the final
/// authority; Backend's cached availability is only an optimistic UI filter.
/// </summary>
public sealed class CompatibilityResult
{
    public bool IsCompatible { get; init; }

    /// <summary>Human-readable reason when not compatible (shown in the UI / ACK error).</summary>
    public string? Reason { get; init; }

    public static CompatibilityResult Compatible() => new() { IsCompatible = true };

    public static CompatibilityResult Incompatible(string reason) =>
        new() { IsCompatible = false, Reason = reason };
}
