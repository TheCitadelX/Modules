namespace CitadelX.Backend.Options;

public sealed class CoreRepoOptions
{
    public required GitHubRepo Singbox { get; init; }
    public required GitHubRepo SingboxExtended { get; init; }
}

public sealed class GitHubRepo
{
    public required string Owner { get; init; }
    public required string Repo { get; init; }
}
