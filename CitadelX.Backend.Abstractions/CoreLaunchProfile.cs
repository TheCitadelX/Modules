namespace CitadelX.Backend.Cores;

public sealed class CoreLaunchProfile
{
    public string? ArgumentsTemplate { get; init; }
    public bool UseRunCommand { get; init; } = true;
    public string? WorkingDirectory { get; init; }
}
