namespace CitadelX.Backend.Options;

/// <summary>
/// Optional per-core GitHub repo overrides, keyed by core Id (case-insensitive). Carries no
/// core-specific names: a module supplies its own default repo and looks its override up by Id,
/// so adding a core never edits this abstraction (§12 modularity). Bound from the
/// <c>CoreRepos</c> configuration section, whose keys are core Ids.
/// </summary>
public sealed class CoreRepoOptions
{
    private readonly IReadOnlyDictionary<string, GitHubRepo> _repos;

    public CoreRepoOptions() : this(null)
    {
    }

    public CoreRepoOptions(IDictionary<string, GitHubRepo>? repos)
    {
        _repos = repos is null
            ? new Dictionary<string, GitHubRepo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GitHubRepo>(repos, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The configured repo override for a core, or null to use the module's own default.</summary>
    public GitHubRepo? Resolve(string coreId) =>
        _repos.TryGetValue(coreId, out var repo) ? repo : null;
}

public sealed class GitHubRepo
{
    public required string Owner { get; init; }
    public required string Repo { get; init; }
}
