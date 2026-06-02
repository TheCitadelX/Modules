using System.Text;
using System.Text.Json.Nodes;

namespace CitadelX.SingboxExtendedModule;

/// <summary>
/// Builds client subscription links from a SingboxExtended native config. SingboxExtended owns its
/// copy of this logic so the fork can diverge from the base Singbox module independently.
/// </summary>
public static class SingboxExtendedSubscriptionBuilder
{
    public static IReadOnlyList<string> Build(string config, string userId, string host, string label, string? credentialsJson = null)
    {
        var credentials = ParseCredentials(credentialsJson);
        var links = new List<string>();

        foreach (var inbound in ExtractInbounds(config, userId, credentials))
        {
            var inboundLinks = inbound.Type.ToLowerInvariant() switch
            {
                "mixed" => BuildMixedLinks(inbound, host, label, userId),
                "socks" => Single(BuildSocksLink(inbound, host, label, userId)),
                "http" => Single(BuildHttpLink(inbound, host, label, userId)),
                "vless" => Single(BuildVlessLink(inbound, host, label)),
                "vmess" => Single(BuildVmessLink(inbound, host, label)),
                "trojan" => Single(BuildTrojanLink(inbound, host, label)),
                "shadowsocks" or "ss" => Single(BuildShadowsocksLink(inbound, host, label)),
                "hysteria2" or "hy2" => Single(BuildHysteria2Link(inbound, host, label)),
                "tuic" => Single(BuildTuicLink(inbound, host, label)),
                _ => Array.Empty<string>()
            };

            foreach (var link in inboundLinks)
            {
                if (!string.IsNullOrWhiteSpace(link))
                {
                    links.Add(link);
                }
            }
        }

        return links;
    }

    private static IReadOnlyList<string> BuildMixedLinks(InboundUserInfo inbound, string host, string label, string userId)
    {
        var links = new[]
        {
            BuildSocksLink(inbound, host, label, userId),
            BuildHttpLink(inbound, host, label, userId)
        };
        return links.Where(link => !string.IsNullOrWhiteSpace(link)).ToArray();
    }

    private static IReadOnlyList<string> Single(string value)
        => string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };

    public static string? BuildClientConfig(string config, string userId, string host, string label, string? credentialsJson = null)
    {
        var credentials = ParseCredentials(credentialsJson);
        var proxy = ExtractInbounds(config, userId, credentials)
            .FirstOrDefault(inbound => IsProxyInbound(inbound.Type));
        if (proxy is null)
        {
            return null;
        }

        var outboundType = proxy.Type.Equals("mixed", StringComparison.OrdinalIgnoreCase)
            ? "socks"
            : proxy.Type.ToLowerInvariant();
        var outbound = new JsonObject
        {
            ["type"] = outboundType,
            ["tag"] = "citadelx",
            ["server"] = host,
            ["server_port"] = proxy.Port
        };

        if (!string.IsNullOrWhiteSpace(proxy.Password))
        {
            outbound["username"] = userId;
            outbound["password"] = proxy.Password;
        }

        return new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "info" },
            ["outbounds"] = new JsonArray
            {
                outbound,
                new JsonObject { ["type"] = "direct", ["tag"] = "direct" }
            },
            ["route"] = new JsonObject { ["final"] = "citadelx" }
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject? ParseCredentials(string? credentialsJson)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(credentialsJson) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<InboundUserInfo> ExtractInbounds(string configJson, string externalId, JsonObject? credentials)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(configJson);
        }
        catch
        {
            yield break;
        }

        if (root?["inbounds"] is not JsonArray inbounds)
        {
            yield break;
        }

        var credentialUuid = FirstString(credentials, "uuid", "id");
        var credentialPassword = FirstString(credentials, "password", "passwd");
        var credentialMethod = FirstString(credentials, "method", "cipher");
        var credentialFlow = FirstString(credentials, "flow");

        foreach (var inboundNode in inbounds)
        {
            if (inboundNode is not JsonObject inbound)
            {
                continue;
            }

            var type = GetString(inbound["type"]);
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            var listenPort = GetInt(inbound["listen_port"])
                ?? GetInt(inbound["port"])
                ?? GetInt(inbound["listenPort"]);
            if (listenPort is null)
            {
                continue;
            }

            var configUser = FindConfigUser(inbound, ResolveUserKey(type), externalId);
            var uuid = FirstNonEmpty(credentialUuid, FirstString(configUser, "uuid", "id"));
            var password = FirstNonEmpty(credentialPassword, FirstString(configUser, "password", "passwd"));
            var method = FirstNonEmpty(credentialMethod, FirstString(configUser, "method", "cipher"), GetString(inbound["method"]));
            var flow = FirstNonEmpty(credentialFlow, FirstString(configUser, "flow"));

            yield return new InboundUserInfo
            {
                Type = type,
                Tag = GetString(inbound["tag"]),
                Port = listenPort.Value,
                Uuid = uuid,
                Password = password,
                Method = method,
                Flow = flow,
                Tls = inbound["tls"] as JsonObject,
                Transport = inbound["transport"] as JsonObject
            };
        }
    }

    private static JsonObject? FindConfigUser(JsonObject inbound, string userKey, string externalId)
    {
        if (inbound["users"] is not JsonArray users)
        {
            return null;
        }

        foreach (var userNode in users)
        {
            if (userNode is not JsonObject user)
            {
                continue;
            }

            if (Matches(user, userKey, externalId)
                || Matches(user, "name", externalId)
                || Matches(user, "username", externalId))
            {
                return user;
            }
        }

        return null;
    }

    private static bool Matches(JsonObject obj, string key, string value)
        => string.Equals(GetString(obj[key]), value, StringComparison.OrdinalIgnoreCase);

    private static string BuildVlessLink(InboundUserInfo inbound, string host, string label)
    {
        if (string.IsNullOrWhiteSpace(inbound.Uuid))
        {
            return string.Empty;
        }

        var query = new List<string> { "encryption=none" };
        AddTransportQuery(query, inbound);
        AddTlsQuery(query, inbound);

        if (!string.IsNullOrWhiteSpace(inbound.Flow))
        {
            query.Add($"flow={Escape(inbound.Flow)}");
        }

        return $"vless://{Escape(inbound.Uuid)}@{host}:{inbound.Port}?{string.Join("&", query)}#{Escape(LinkLabel(label, inbound))}";
    }

    private static string BuildVmessLink(InboundUserInfo inbound, string host, string label)
    {
        if (string.IsNullOrWhiteSpace(inbound.Uuid))
        {
            return string.Empty;
        }

        var transportType = GetTransportType(inbound);
        var tlsEnabled = GetBool(inbound.Tls?["enabled"]);
        var vmess = new JsonObject
        {
            ["v"] = "2",
            ["ps"] = LinkLabel(label, inbound),
            ["add"] = host,
            ["port"] = inbound.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["id"] = inbound.Uuid,
            ["aid"] = "0",
            ["scy"] = "auto",
            ["net"] = transportType,
            ["type"] = "none",
            ["tls"] = tlsEnabled ? "tls" : string.Empty
        };

        var sni = GetString(inbound.Tls?["server_name"]);
        if (!string.IsNullOrWhiteSpace(sni))
        {
            vmess["sni"] = sni;
        }

        ApplyVmessTransport(vmess, inbound, transportType);
        var content = vmess.ToJsonString();
        return $"vmess://{Convert.ToBase64String(Encoding.UTF8.GetBytes(content))}";
    }

    private static string BuildTrojanLink(InboundUserInfo inbound, string host, string label)
    {
        if (string.IsNullOrWhiteSpace(inbound.Password))
        {
            return string.Empty;
        }

        var query = new List<string>();
        AddTransportQuery(query, inbound);
        AddTlsQuery(query, inbound);
        return $"trojan://{Escape(inbound.Password)}@{host}:{inbound.Port}{Query(query)}#{Escape(LinkLabel(label, inbound))}";
    }

    private static string BuildSocksLink(InboundUserInfo inbound, string host, string label, string userId)
    {
        if (string.IsNullOrWhiteSpace(inbound.Password))
        {
            return $"socks5://{host}:{inbound.Port}#{Escape(LinkLabel(label, inbound))}";
        }

        return $"socks5://{Escape(userId)}:{Escape(inbound.Password)}@{host}:{inbound.Port}#{Escape(LinkLabel(label, inbound))}";
    }

    private static string BuildHttpLink(InboundUserInfo inbound, string host, string label, string userId)
    {
        var scheme = GetBool(inbound.Tls?["enabled"]) ? "https" : "http";
        if (string.IsNullOrWhiteSpace(inbound.Password))
        {
            return $"{scheme}://{host}:{inbound.Port}#{Escape(LinkLabel(label, inbound))}";
        }

        return $"{scheme}://{Escape(userId)}:{Escape(inbound.Password)}@{host}:{inbound.Port}#{Escape(LinkLabel(label, inbound))}";
    }

    private static string BuildShadowsocksLink(InboundUserInfo inbound, string host, string label)
    {
        if (string.IsNullOrWhiteSpace(inbound.Method) || string.IsNullOrWhiteSpace(inbound.Password))
        {
            return string.Empty;
        }

        var userInfo = Base64Url($"{inbound.Method}:{inbound.Password}");
        return $"ss://{userInfo}@{host}:{inbound.Port}#{Escape(LinkLabel(label, inbound))}";
    }

    private static string BuildHysteria2Link(InboundUserInfo inbound, string host, string label)
    {
        if (string.IsNullOrWhiteSpace(inbound.Password))
        {
            return string.Empty;
        }

        var query = new List<string>();
        var sni = GetString(inbound.Tls?["server_name"]);
        if (!string.IsNullOrWhiteSpace(sni))
        {
            query.Add($"sni={Escape(sni)}");
        }

        return $"hysteria2://{Escape(inbound.Password)}@{host}:{inbound.Port}{Query(query)}#{Escape(LinkLabel(label, inbound))}";
    }

    private static string BuildTuicLink(InboundUserInfo inbound, string host, string label)
    {
        if (string.IsNullOrWhiteSpace(inbound.Uuid) || string.IsNullOrWhiteSpace(inbound.Password))
        {
            return string.Empty;
        }

        var query = new List<string>();
        var congestionControl = GetString(inbound.Transport?["congestion_control"])
            ?? GetString(inbound.Transport?["congestionControl"]);
        if (!string.IsNullOrWhiteSpace(congestionControl))
        {
            query.Add($"congestion_control={Escape(congestionControl)}");
        }

        var sni = GetString(inbound.Tls?["server_name"]);
        if (!string.IsNullOrWhiteSpace(sni))
        {
            query.Add($"sni={Escape(sni)}");
        }

        return $"tuic://{Escape(inbound.Uuid)}:{Escape(inbound.Password)}@{host}:{inbound.Port}{Query(query)}#{Escape(LinkLabel(label, inbound))}";
    }

    private static void AddTlsQuery(List<string> query, InboundUserInfo inbound)
    {
        var tlsEnabled = GetBool(inbound.Tls?["enabled"]);
        var reality = inbound.Tls?["reality"] as JsonObject;
        var realityEnabled = GetBool(reality?["enabled"]);
        if (!tlsEnabled)
        {
            return;
        }

        query.Add(realityEnabled ? "security=reality" : "security=tls");
        AddQueryValue(query, "sni", GetString(inbound.Tls?["server_name"]));
        AddQueryValue(query, "alpn", JoinArray(inbound.Tls?["alpn"] as JsonArray));
        AddQueryValue(query, "fp", GetString(inbound.Tls?["utls"]?["fingerprint"])
            ?? GetString(inbound.Tls?["fingerprint"])
            ?? GetString(inbound.Tls?["fp"]));

        if (realityEnabled)
        {
            AddQueryValue(query, "pbk", GetString(reality?["public_key"]) ?? GetString(reality?["publicKey"]));
            AddQueryValue(query, "sid", GetString(reality?["short_id"]) ?? GetString(reality?["shortId"]));
        }
    }

    private static void AddTransportQuery(List<string> query, InboundUserInfo inbound)
    {
        var transportType = GetTransportType(inbound);
        AddQueryValue(query, "type", transportType);

        if (string.Equals(transportType, "ws", StringComparison.OrdinalIgnoreCase))
        {
            AddQueryValue(query, "path", GetString(inbound.Transport?["path"]));
            AddQueryValue(query, "host", GetString(inbound.Transport?["headers"]?["Host"])
                ?? GetString(inbound.Transport?["host"]));
        }

        if (string.Equals(transportType, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transportType, "httpupgrade", StringComparison.OrdinalIgnoreCase))
        {
            AddQueryValue(query, "path", GetString(inbound.Transport?["path"]));
            AddQueryValue(query, "host", JoinArray(inbound.Transport?["host"] as JsonArray)
                ?? GetString(inbound.Transport?["host"]));
            AddQueryValue(query, "method", GetString(inbound.Transport?["method"]));
        }

        if (string.Equals(transportType, "grpc", StringComparison.OrdinalIgnoreCase))
        {
            AddQueryValue(query, "serviceName", GetString(inbound.Transport?["service_name"]));
        }

        AddQueryValue(query, "headerType", GetString(inbound.Transport?["header"]?["type"])
            ?? GetString(inbound.Transport?["header_type"]));
    }

    private static void ApplyVmessTransport(JsonObject vmess, InboundUserInfo inbound, string transportType)
    {
        if (string.Equals(transportType, "ws", StringComparison.OrdinalIgnoreCase))
        {
            vmess["path"] = GetString(inbound.Transport?["path"]) ?? string.Empty;
            vmess["host"] = GetString(inbound.Transport?["headers"]?["Host"])
                ?? GetString(inbound.Transport?["host"])
                ?? string.Empty;
        }
        else if (string.Equals(transportType, "grpc", StringComparison.OrdinalIgnoreCase))
        {
            vmess["path"] = GetString(inbound.Transport?["service_name"]) ?? string.Empty;
        }
        else
        {
            vmess["path"] = GetString(inbound.Transport?["path"]) ?? string.Empty;
            vmess["host"] = JoinArray(inbound.Transport?["host"] as JsonArray)
                ?? GetString(inbound.Transport?["host"])
                ?? string.Empty;
        }
    }

    private static string ResolveUserKey(string type)
        => type.ToLowerInvariant() switch
        {
            "mixed" or "socks" or "http" => "username",
            _ => "name"
        };

    private static bool IsProxyInbound(string type)
        => type.Equals("mixed", StringComparison.OrdinalIgnoreCase)
           || type.Equals("socks", StringComparison.OrdinalIgnoreCase)
           || type.Equals("http", StringComparison.OrdinalIgnoreCase);

    private static string GetTransportType(InboundUserInfo inbound)
        => GetString(inbound.Transport?["type"]) ?? "tcp";

    private static void AddQueryValue(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{key}={Escape(value)}");
        }
    }

    private static string Query(IReadOnlyList<string> query)
        => query.Count > 0 ? $"?{string.Join("&", query)}" : string.Empty;

    private static string LinkLabel(string label, InboundUserInfo inbound)
        => !string.IsNullOrWhiteSpace(inbound.Tag) ? $"{label} {inbound.Tag}" : $"{label} {inbound.Type}:{inbound.Port}";

    private static string? JoinArray(JsonArray? array)
    {
        if (array is null)
        {
            return null;
        }

        var values = array.Select(GetString)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return values.Length > 0 ? string.Join(",", values) : null;
    }

    private static string? FirstString(JsonObject? obj, params string[] keys)
    {
        if (obj is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            var value = GetString(obj[key]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? GetString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return node.ToString();
        }
    }

    private static int? GetInt(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            return int.TryParse(GetString(node), out var value) ? value : null;
        }
    }

    private static bool GetBool(JsonNode? node)
    {
        if (node is null)
        {
            return false;
        }

        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            return bool.TryParse(GetString(node), out var value) && value;
        }
    }

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Escape(string value)
        => Uri.EscapeDataString(value);

    private sealed class InboundUserInfo
    {
        public required string Type { get; init; }
        public string? Tag { get; init; }
        public required int Port { get; init; }
        public string? Uuid { get; init; }
        public string? Password { get; init; }
        public string? Method { get; init; }
        public string? Flow { get; init; }
        public JsonObject? Tls { get; init; }
        public JsonObject? Transport { get; init; }
    }
}
