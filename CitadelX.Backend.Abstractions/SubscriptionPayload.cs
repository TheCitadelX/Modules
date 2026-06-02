namespace CitadelX.Backend.Cores;

public enum SubscriptionPayloadKind
{
    UriList,
    ConfigFile,
    Combined
}

/// <summary>
/// Typed subscription output for one server/user pair. URI-list subscriptions remain the default,
/// while modules that need to ship a full client config can return a file payload.
/// </summary>
public sealed class SubscriptionPayload
{
    public required SubscriptionPayloadKind Kind { get; init; }
    public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
    public string? Content { get; init; }
    public string? FileName { get; init; }
    public string ContentType { get; init; } = "text/plain";

    public static SubscriptionPayload UriList(IEnumerable<string> links)
        => new()
        {
            Kind = SubscriptionPayloadKind.UriList,
            Links = links.Where(link => !string.IsNullOrWhiteSpace(link)).ToArray()
        };

    public static SubscriptionPayload ConfigFile(string fileName, string content, string contentType = "text/plain")
        => new()
        {
            Kind = SubscriptionPayloadKind.ConfigFile,
            FileName = fileName,
            Content = content,
            ContentType = contentType
        };

    public static SubscriptionPayload Combined(
        IEnumerable<string> links,
        string fileName,
        string content,
        string contentType = "text/plain")
        => new()
        {
            Kind = SubscriptionPayloadKind.Combined,
            Links = links.Where(link => !string.IsNullOrWhiteSpace(link)).ToArray(),
            FileName = fileName,
            Content = content,
            ContentType = contentType
        };
}
