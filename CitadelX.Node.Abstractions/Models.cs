namespace CitadelX.Node.Abstractions;

public sealed class UserEntity
{
    public required string Id { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsOnline { get; init; }
    public long Traffic { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; init; }
}

public sealed class ServerLaunchProfile
{
    public string? ServerId { get; set; }
    public string? CoreId { get; set; }
    public string? BinaryPath { get; set; }
    public string? Arguments { get; set; }
    public bool? UseRunCommand { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? ConfigPath { get; set; }
    public bool ShouldBeRunning { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
