namespace CitadelX.Backend.Cores;

/// <summary>
/// Module hook input for normalizing or generating per-user credentials at attach time.
/// The returned JSON is stored in ServerUserEntity.UserTemplateJson and sent to the node
/// as userTemplate. Resource allocation is separate and stays in ResourceAllocationJson.
/// </summary>
public sealed class UserTemplateRequest
{
    public required string UserId { get; init; }
    public string? UserTemplateJson { get; init; }
    public string? ResourceAllocationJson { get; init; }
}
