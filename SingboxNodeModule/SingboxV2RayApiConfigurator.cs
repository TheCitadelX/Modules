using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CitadelX.SingboxNodeModule;

public sealed class SingboxV2RayApiConfiguration
{
    public required string ListenAddress { get; init; }
    public required IReadOnlyList<string> UserIds { get; init; }
    public bool Changed { get; init; }
}

public static class SingboxV2RayApiConfigurator
{
    private const int FirstPort = 30000;
    private const int PortRange = 20000;

    public static SingboxV2RayApiConfiguration Configure(
        JsonNode root,
        string serverId,
        string? preferredListenAddress = null,
        bool reuseExistingListenAddress = true)
    {
        if (root is not JsonObject rootObject)
        {
            throw new InvalidOperationException("Config root must be an object.");
        }

        var before = rootObject.ToJsonString();
        var userIds = CollectUserIds(rootObject);
        var experimental = rootObject["experimental"] as JsonObject ?? new JsonObject();
        rootObject["experimental"] = experimental;

        var v2rayApi = experimental["v2ray_api"] as JsonObject ?? new JsonObject();
        experimental["v2ray_api"] = v2rayApi;

        var existingListenAddress = ReadString(v2rayApi, "listen");
        var listenAddress = ResolveListenAddress(
            serverId,
            preferredListenAddress,
            reuseExistingListenAddress ? existingListenAddress : null);
        v2rayApi["listen"] = listenAddress;

        var stats = v2rayApi["stats"] as JsonObject ?? new JsonObject();
        v2rayApi["stats"] = stats;
        stats["enabled"] = true;
        stats["users"] = new JsonArray(
            userIds.Select(userId => (JsonNode?)JsonValue.Create(userId)).ToArray());

        return new SingboxV2RayApiConfiguration
        {
            ListenAddress = listenAddress,
            UserIds = userIds,
            Changed = !string.Equals(before, rootObject.ToJsonString(), StringComparison.Ordinal)
        };
    }

    private static IReadOnlyList<string> CollectUserIds(JsonObject root)
    {
        if (root["inbounds"] is not JsonArray inbounds)
        {
            return Array.Empty<string>();
        }

        var userIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inboundNode in inbounds)
        {
            if (inboundNode is not JsonObject inbound || inbound["users"] is not JsonArray users)
            {
                continue;
            }

            foreach (var userNode in users)
            {
                if (userNode is not JsonObject user)
                {
                    continue;
                }

                var userId = ReadString(user, "name") ?? ReadString(user, "username");
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    userIds.Add(userId);
                }
            }
        }

        return userIds.Order(StringComparer.Ordinal).ToArray();
    }

    private static string? ReadString(JsonObject value, string propertyName)
    {
        try
        {
            return value[propertyName]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string ResolveListenAddress(
        string serverId,
        string? preferredListenAddress,
        string? existingListenAddress)
    {
        if (TryParseLoopbackAddress(preferredListenAddress, out var preferred))
        {
            return preferred;
        }

        if (TryParseLoopbackAddress(existingListenAddress, out var existing))
        {
            return existing;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serverId));
        var startPort = FirstPort + (BitConverter.ToUInt16(hash, 0) % PortRange);
        for (var offset = 0; offset < PortRange; offset++)
        {
            var port = FirstPort + ((startPort - FirstPort + offset) % PortRange);
            if (CanBind(port))
            {
                return $"127.0.0.1:{port}";
            }
        }

        throw new InvalidOperationException("No local port is available for the sing-box V2Ray API.");
    }

    private static bool TryParseLoopbackAddress(string? value, out string address)
    {
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separator = value.LastIndexOf(':');
        if (separator <= 0
            || !IPAddress.TryParse(value[..separator], out var ipAddress)
            || !IPAddress.IsLoopback(ipAddress)
            || !int.TryParse(value[(separator + 1)..], out var port)
            || port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            return false;
        }

        address = $"{ipAddress}:{port}";
        return true;
    }

    private static bool CanBind(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
