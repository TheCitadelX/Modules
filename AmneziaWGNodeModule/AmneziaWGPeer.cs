using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Node.Abstractions;

namespace CitadelX.AmneziaWGNodeModule;

internal sealed class AmneziaWGPeer
{
    public required string UserId { get; init; }
    public required string PublicKey { get; init; }
    public required string AllowedIps { get; init; }
    public string? PresharedKey { get; init; }
    public string? Endpoint { get; init; }
    public string? PersistentKeepalive { get; init; }

    public static AmneziaWGPeer From(UserEntity user, JsonObject? template)
    {
        var resources = ParseObject(user.ResourceAllocationJson);
        var publicKey = GetString(template, "publicKey");
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            throw new InvalidOperationException("AmneziaWG user template must contain publicKey.");
        }

        var allowedIps = GetString(template, "allowedIps")
            ?? GetString(resources, "peerAddress")
            ?? throw new InvalidOperationException("AmneziaWG user requires an allocated peerAddress.");

        return new AmneziaWGPeer
        {
            UserId = user.Id,
            PublicKey = publicKey,
            AllowedIps = allowedIps,
            PresharedKey = GetString(template, "presharedKey"),
            Endpoint = GetString(template, "endpoint"),
            PersistentKeepalive = GetString(template, "persistentKeepalive")
        };
    }

    private static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonObject? obj, string key)
    {
        if (obj is null || obj[key] is null)
        {
            return null;
        }

        try
        {
            return obj[key]!.GetValue<string>();
        }
        catch
        {
            return obj[key]!.ToString();
        }
    }
}
