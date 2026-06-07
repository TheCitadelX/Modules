using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CitadelX.SingboxModule;

/// <summary>
/// Generates sing-box native JSON from the admin's structured "simple setup" input.
/// This is a faithful port of the Frontend's former <c>buildSingboxConfig()</c> (servers/index.vue):
/// the Frontend now only collects values into a <see cref="JsonObject"/> and posts them; the
/// authoritative native-config generation lives here, on the Backend (MODULE_SYSTEM_SPEC §7.2).
///
/// Structured keys use the same camelCase names the Frontend form used (inboundType, inboundListen,
/// outboundType, routeFinal, ...), so the form values map 1:1 onto this generator.
/// </summary>
public static class SingboxConfigBuilder
{
    private const string InboundTag = "main-in";
    private const string OutboundTag = "main-out";

    // Match JSON.stringify(config, null, 2): 2-space indent and minimal escaping
    // (don't \u-escape <, >, &, +, or non-ASCII the way the default encoder does).
    // Writing through Utf8JsonWriter (rather than ToJsonString with custom
    // JsonSerializerOptions) keeps the DOM's default TypeInfoResolver, so JsonValues
    // created via JsonArray.Add<T>(string) still serialize.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(JsonObject s)
    {
        var inbound = BuildInbound(s);
        var outbound = BuildOutbound(s);
        var route = BuildRoute(s, outbound, out var outbounds);

        var config = new JsonObject
        {
            ["inbounds"] = new JsonArray(inbound),
            ["outbounds"] = outbounds,
            ["route"] = route,
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            config.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonObject BuildInbound(JsonObject s)
    {
        var type = ResolveInboundType(s);
        var inbound = new JsonObject
        {
            ["type"] = type,
            ["tag"] = InboundTag,
            ["listen"] = Str(s, "inboundListen"),
            ["listen_port"] = NumNode(Str(s, "inboundPort")),
        };

        AddTrimmed(inbound, "bind_interface", Str(s, "inboundBindInterface"));
        if (HasText(s, "inboundRoutingMark")) inbound["routing_mark"] = NumNode(Str(s, "inboundRoutingMark"));
        if (Bool(s, "inboundReuseAddr")) inbound["reuse_addr"] = true;
        AddTrimmed(inbound, "netns", Str(s, "inboundNetns"));
        if (Bool(s, "inboundTcpFastOpen")) inbound["tcp_fast_open"] = true;
        if (Bool(s, "inboundTcpMultiPath")) inbound["tcp_multi_path"] = true;
        if (Bool(s, "inboundDisableTcpKeepAlive")) inbound["tcp_keep_alive"] = false;
        AddTrimmed(inbound, "tcp_keep_alive", Str(s, "inboundTcpKeepAlive"));
        AddTrimmed(inbound, "tcp_keep_alive_interval", Str(s, "inboundTcpKeepAliveInterval"));
        if (Bool(s, "inboundUdpFragment")) inbound["udp_fragment"] = true;
        AddTrimmed(inbound, "udp_timeout", Str(s, "inboundUdpTimeout"));
        AddTrimmed(inbound, "detour", Str(s, "inboundDetour"));

        var network = Str(s, "inboundNetwork");
        if (!string.IsNullOrEmpty(network) && network != "both") inbound["network"] = network;

        if (type == "direct")
        {
            AddTrimmed(inbound, "override_address", Str(s, "inboundOverrideAddress"));
            if (HasText(s, "inboundOverridePort")) inbound["override_port"] = NumNode(Str(s, "inboundOverridePort"));
        }

        if (type is "mixed" or "socks" or "http")
        {
            var username = Str(s, "inboundProxyUsername").Trim();
            var password = Str(s, "inboundPassword").Trim();
            if (username.Length > 0 || password.Length > 0)
            {
                inbound["users"] = new JsonArray(new JsonObject
                {
                    ["username"] = username,
                    ["password"] = password,
                });
            }
            if (Bool(s, "inboundSetSystemProxy")) inbound["set_system_proxy"] = true;
        }

        if (type == "shadowsocks")
        {
            inbound["method"] = Str(s, "inboundMethod");
            AddTrimmed(inbound, "password", Str(s, "inboundPassword"));
        }

        if (type == "vmess")
        {
            var user = new JsonObject();
            var uuid = Str(s, "inboundUserUuid").Trim();
            if (uuid.Length > 0)
            {
                AddTrimmed(user, "name", Str(s, "inboundUserName"));
                user["uuid"] = uuid;
                if (JsNumber(Str(s, "inboundAlterId")) > 0) user["alterId"] = NumNode(Str(s, "inboundAlterId"));
            }
            inbound["users"] = user.Count > 0 ? new JsonArray(user) : new JsonArray();
        }

        if (type == "trojan")
        {
            var user = new JsonObject();
            var password = Str(s, "inboundUserPassword").Trim();
            if (password.Length > 0)
            {
                AddTrimmed(user, "name", Str(s, "inboundUserName"));
                user["password"] = password;
            }
            inbound["users"] = user.Count > 0 ? new JsonArray(user) : new JsonArray();
        }

        if (type == "vless")
        {
            var user = new JsonObject();
            var uuid = Str(s, "inboundUserUuid").Trim();
            if (uuid.Length > 0)
            {
                AddTrimmed(user, "name", Str(s, "inboundUserName"));
                user["uuid"] = uuid;
                AddTrimmed(user, "flow", Str(s, "inboundUserFlow"));
            }
            inbound["users"] = user.Count > 0 ? new JsonArray(user) : new JsonArray();
        }

        if (type is "vless" or "trojan" or "vmess")
        {
            var tls = BuildTls(
                enabled: IsInboundTlsEnabled(s),
                serverName: Str(s, "inboundTlsServerName"),
                alpn: Str(s, "inboundTlsAlpn"),
                certificatePath: Str(s, "inboundTlsCertificatePath"),
                keyPath: Str(s, "inboundTlsKeyPath"),
                insecure: false);
            AddRealityIfNeeded(tls, s);
            if (tls is not null) inbound["tls"] = tls;
            var transport = BuildTransport(s, "inbound");
            if (transport is not null) inbound["transport"] = transport;
        }

        return inbound;
    }

    private static JsonObject BuildOutbound(JsonObject s)
    {
        var type = ResolveOutboundType(s);
        var outbound = new JsonObject
        {
            ["type"] = type,
            ["tag"] = OutboundTag,
        };

        if (type is not ("direct" or "block"))
        {
            outbound["server"] = Str(s, "outboundServer");
            outbound["server_port"] = NumNode(Str(s, "outboundPort"));
        }

        if (type == "socks")
        {
            outbound["version"] = Str(s, "outboundVersion");
            var username = Str(s, "outboundUsername").Trim();
            var password = Str(s, "outboundPassword").Trim();
            if (username.Length > 0) outbound["username"] = username;
            if (password.Length > 0) outbound["password"] = password;
        }

        if (type == "http")
        {
            AddTrimmed(outbound, "username", Str(s, "outboundUsername"));
            AddTrimmed(outbound, "password", Str(s, "outboundPassword"));
            AddTrimmed(outbound, "path", Str(s, "outboundPath"));
            var headers = ParseHeaders(Str(s, "outboundHeadersJson"));
            if (headers is not null) outbound["headers"] = headers;
        }

        if (type == "shadowsocks")
        {
            outbound["method"] = Str(s, "outboundMethod");
            outbound["password"] = Str(s, "outboundPassword").Trim();
        }

        if (type == "vmess")
        {
            outbound["uuid"] = Str(s, "outboundVmessUuid").Trim();
            outbound["security"] = Str(s, "outboundVmessSecurity");
            if (JsNumber(Str(s, "outboundVmessAlterId")) > 0) outbound["alter_id"] = NumNode(Str(s, "outboundVmessAlterId"));
            if (Bool(s, "outboundVmessGlobalPadding")) outbound["global_padding"] = true;
            outbound["authenticated_length"] = Bool(s, "outboundVmessAuthenticatedLength");
        }

        if (type == "trojan")
        {
            outbound["password"] = Str(s, "outboundPassword").Trim();
        }

        if (type == "vless")
        {
            outbound["uuid"] = Str(s, "outboundVlessUuid").Trim();
            var flow = Str(s, "outboundVlessFlow").Trim();
            if (flow.Length > 0) outbound["flow"] = flow;
        }

        if (type is "socks" or "http" or "shadowsocks" or "vmess" or "trojan" or "vless" or "direct")
        {
            var network = Str(s, "outboundNetwork");
            if (!string.IsNullOrEmpty(network) && network != "both") outbound["network"] = network;
        }

        if (type is "vmess" or "vless")
        {
            var packetEncoding = Str(s, "outboundPacketEncoding").Trim();
            if (packetEncoding.Length > 0) outbound["packet_encoding"] = packetEncoding;
        }

        if (type is "http" or "vmess" or "trojan" or "vless")
        {
            var tls = BuildTls(
                enabled: Bool(s, "outboundTlsEnabled"),
                serverName: Str(s, "outboundTlsServerName"),
                alpn: Str(s, "outboundTlsAlpn"),
                certificatePath: null,
                keyPath: null,
                insecure: Bool(s, "outboundTlsInsecure"));
            if (tls is not null) outbound["tls"] = tls;
        }

        if (type is "vmess" or "trojan" or "vless")
        {
            var transport = BuildTransport(s, "outbound");
            if (transport is not null) outbound["transport"] = transport;
        }

        AddTrimmed(outbound, "domain_resolver", Str(s, "outboundDomainResolver"));
        if (Str(s, "outboundDetour").Trim().Length > 0) outbound["detour"] = Str(s, "outboundDetour").Trim();
        if (Str(s, "outboundBindInterface").Trim().Length > 0) outbound["bind_interface"] = Str(s, "outboundBindInterface").Trim();
        AddTrimmed(outbound, "inet4_bind_address", Str(s, "outboundInet4BindAddress"));
        AddTrimmed(outbound, "inet6_bind_address", Str(s, "outboundInet6BindAddress"));
        if (Bool(s, "outboundBindAddressNoPort")) outbound["bind_address_no_port"] = true;
        if (HasText(s, "outboundRoutingMark")) outbound["routing_mark"] = NumNode(Str(s, "outboundRoutingMark"));
        if (Bool(s, "outboundReuseAddr")) outbound["reuse_addr"] = true;
        AddTrimmed(outbound, "netns", Str(s, "outboundNetns"));
        if (Bool(s, "outboundTcpFastOpen")) outbound["tcp_fast_open"] = true;
        if (Bool(s, "outboundTcpMultiPath")) outbound["tcp_multi_path"] = true;
        if (Bool(s, "outboundDisableTcpKeepAlive")) outbound["tcp_keep_alive"] = false;
        AddTrimmed(outbound, "tcp_keep_alive", Str(s, "outboundTcpKeepAlive"));
        AddTrimmed(outbound, "tcp_keep_alive_interval", Str(s, "outboundTcpKeepAliveInterval"));
        if (Bool(s, "outboundUdpFragment")) outbound["udp_fragment"] = true;
        AddTrimmed(outbound, "connect_timeout", Str(s, "outboundConnectTimeout"));

        return outbound;
    }

    private static JsonObject BuildRoute(JsonObject s, JsonObject outbound, out JsonArray outbounds)
    {
        var directDomains = SplitList(Str(s, "routeDirectDomains"));
        var directIpCidrs = SplitList(Str(s, "routeDirectIpCidrs"));
        var blockDomains = SplitList(Str(s, "routeBlockDomains"));
        var blockIpCidrs = SplitList(Str(s, "routeBlockIpCidrs"));
        var routeFinal = Str(s, "routeFinal");

        var usesDirectOutbound = directDomains.Count > 0 || directIpCidrs.Count > 0 || routeFinal == "direct";
        var usesBlockOutbound = blockDomains.Count > 0 || blockIpCidrs.Count > 0 || routeFinal == "block";

        outbounds = new JsonArray(outbound);
        if (usesDirectOutbound) outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = "direct" });
        if (usesBlockOutbound) outbounds.Add(new JsonObject { ["type"] = "block", ["tag"] = "block" });

        var routeRules = new JsonArray();

        if (Bool(s, "routeSniffEnabled"))
        {
            var rule = new JsonObject { ["action"] = "sniff" };
            AddTrimmed(rule, "timeout", Str(s, "routeSniffTimeout"));
            routeRules.Add(rule);
        }

        if (Bool(s, "routeResolveEnabled"))
        {
            var rule = new JsonObject { ["action"] = "resolve" };
            AddTrimmed(rule, "server", Str(s, "routeResolveServer"));
            AddTrimmed(rule, "strategy", Str(s, "routeResolveStrategy"));
            routeRules.Add(rule);
        }

        if (directDomains.Count > 0 || directIpCidrs.Count > 0)
        {
            var rule = new JsonObject { ["outbound"] = "direct" };
            if (directDomains.Count > 0) rule["domain"] = directDomains;
            if (directIpCidrs.Count > 0) rule["ip_cidr"] = directIpCidrs;
            routeRules.Add(rule);
        }

        if (blockDomains.Count > 0 || blockIpCidrs.Count > 0)
        {
            var rule = new JsonObject { ["outbound"] = "block" };
            if (blockDomains.Count > 0) rule["domain"] = blockDomains;
            if (blockIpCidrs.Count > 0) rule["ip_cidr"] = blockIpCidrs;
            routeRules.Add(rule);
        }

        var fallbackOutbound = string.IsNullOrEmpty(routeFinal) ? OutboundTag : routeFinal;
        routeRules.Add(new JsonObject
        {
            ["inbound"] = new JsonArray(InboundTag),
            ["outbound"] = fallbackOutbound,
        });

        var route = new JsonObject { ["rules"] = routeRules };
        if (Bool(s, "routeAutoDetectInterface")) route["auto_detect_interface"] = true;
        AddTrimmed(route, "default_interface", Str(s, "routeDefaultInterface"));
        if (HasText(s, "routeDefaultMark")) route["default_mark"] = NumNode(Str(s, "routeDefaultMark"));
        if (Bool(s, "routeFindProcess")) route["find_process"] = true;
        AddTrimmed(route, "default_domain_resolver", Str(s, "routeDefaultDomainResolver"));
        AddTrimmed(route, "default_network_strategy", Str(s, "routeDefaultNetworkStrategy"));
        AddTrimmed(route, "default_network_type", Str(s, "routeDefaultNetworkType"));
        if (!string.IsNullOrEmpty(routeFinal) && routeFinal != OutboundTag) route["final"] = routeFinal;

        return route;
    }

    private static JsonObject? BuildTls(bool enabled, string serverName, string alpn, string? certificatePath, string? keyPath, bool insecure)
    {
        if (!enabled) return null;
        var tls = new JsonObject { ["enabled"] = true };
        if (serverName.Trim().Length > 0) tls["server_name"] = serverName.Trim();
        var alpnList = SplitCsv(alpn);
        if (alpnList.Count > 0) tls["alpn"] = alpnList;
        if (!string.IsNullOrWhiteSpace(certificatePath)) tls["certificate_path"] = certificatePath!.Trim();
        if (!string.IsNullOrWhiteSpace(keyPath)) tls["key_path"] = keyPath!.Trim();
        if (insecure) tls["insecure"] = true;
        return tls;
    }

    private static void AddRealityIfNeeded(JsonObject? tls, JsonObject s)
    {
        if (tls is null || ResolveInboundSecurity(s) != "reality")
        {
            return;
        }

        var handshakeServer = FirstNonEmpty(Str(s, "inboundRealityHandshakeServer"), Str(s, "inboundTlsServerName"), "www.cloudflare.com")!;
        var handshakePort = JsNumber(FirstNonEmpty(Str(s, "inboundRealityHandshakePort"), "443")!);
        if (!double.IsFinite(handshakePort) || handshakePort < 1)
        {
            handshakePort = 443;
        }

        var privateKey = Str(s, "inboundRealityPrivateKey").Trim();
        if (privateKey.Length == 0)
        {
            privateKey = GenerateRealityPrivateKey();
        }

        var reality = new JsonObject
        {
            ["enabled"] = true,
            ["handshake"] = new JsonObject
            {
                ["server"] = handshakeServer.Trim(),
                ["server_port"] = JsonValue.Create((long)Math.Floor(handshakePort))
            },
            ["private_key"] = privateKey,
            ["short_id"] = FirstNonEmpty(Str(s, "inboundRealityShortId"), GenerateRealityShortId())
        };

        AddTrimmed(reality, "max_time_difference", Str(s, "inboundRealityMaxTimeDifference"));
        tls["reality"] = reality;

        // uTLS fingerprints are client-side settings. The subscription builder emits a sensible
        // Reality URI default, but sing-box rejects tls.utls on inbound server configs.
    }

    private static JsonObject? BuildTransport(JsonObject s, string scope)
    {
        var type = Str(s, scope + "TransportType");
        if (string.IsNullOrEmpty(type)) return null;

        var host = Str(s, scope + "TransportHost");
        var path = Str(s, scope + "TransportPath");
        var method = Str(s, scope + "TransportMethod");
        var grpcServiceName = Str(s, scope + "TransportGrpcServiceName");

        var transport = new JsonObject { ["type"] = type };

        if (type == "http")
        {
            var hosts = SplitCsv(host);
            if (hosts.Count > 0) transport["host"] = hosts;
            if (path.Trim().Length > 0) transport["path"] = path.Trim();
            if (method.Trim().Length > 0) transport["method"] = method.Trim();
        }

        if (type == "ws")
        {
            if (path.Trim().Length > 0) transport["path"] = path.Trim();
            if (host.Trim().Length > 0) transport["headers"] = new JsonObject { ["Host"] = host.Trim() };
        }

        if (type == "httpupgrade")
        {
            if (host.Trim().Length > 0) transport["host"] = host.Trim();
            if (path.Trim().Length > 0) transport["path"] = path.Trim();
        }

        if (type == "grpc" && grpcServiceName.Trim().Length > 0)
        {
            transport["service_name"] = grpcServiceName.Trim();
        }

        return transport;
    }

    private static string ResolveInboundType(JsonObject s)
    {
        return FirstNonEmpty(Str(s, "inboundType"), "mixed")!;
    }

    private static string ResolveOutboundType(JsonObject s)
    {
        return FirstNonEmpty(Str(s, "outboundType"), "direct")!;
    }

    private static string ResolveInboundSecurity(JsonObject s)
    {
        return FirstNonEmpty(Str(s, "inboundSecurity"), Bool(s, "inboundTlsEnabled") ? "tls" : "none")!;
    }

    private static bool IsInboundTlsEnabled(JsonObject s)
    {
        return ResolveInboundSecurity(s) is "tls" or "reality";
    }

    // --- value helpers (mirror the Frontend's trimming / Number() / split semantics) ---

    private static string Str(JsonObject o, string key)
    {
        if (!o.TryGetPropertyValue(key, out var node) || node is null) return "";
        if (node is JsonValue val)
        {
            if (val.TryGetValue<string>(out var sv)) return sv ?? "";
            if (val.TryGetValue<bool>(out var bv)) return bv ? "true" : "false";
            if (val.TryGetValue<double>(out var dv))
            {
                return dv == Math.Floor(dv) && !double.IsInfinity(dv)
                    ? ((long)dv).ToString(CultureInfo.InvariantCulture)
                    : dv.ToString(CultureInfo.InvariantCulture);
            }
        }
        return node.ToString();
    }

    private static bool Bool(JsonObject o, string key)
    {
        if (o.TryGetPropertyValue(key, out var node) && node is JsonValue val)
        {
            if (val.TryGetValue<bool>(out var bv)) return bv;
            if (val.TryGetValue<string>(out var sv)) return string.Equals(sv, "true", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static bool HasText(JsonObject o, string key) => Str(o, key).Trim().Length > 0;

    private static void AddTrimmed(JsonObject target, string key, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > 0) target[key] = trimmed;
    }

    /// <summary>Mirrors JS <c>Number(value)</c>: empty/whitespace → 0, unparseable → NaN.</summary>
    private static double JsNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        return double.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    /// <summary>Numbers serialize as integers when integral (matching JSON.stringify of JS numbers).</summary>
    private static JsonNode NumNode(string raw)
    {
        var d = JsNumber(raw);
        if (!double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Floor(d) && Math.Abs(d) < 9.2e18)
        {
            return JsonValue.Create((long)d);
        }
        return JsonValue.Create(d);
    }

    private static JsonArray SplitCsv(string value)
    {
        var arr = new JsonArray();
        foreach (var part in value.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) arr.Add(trimmed);
        }
        return arr;
    }

    private static JsonArray SplitList(string value)
    {
        var arr = new JsonArray();
        foreach (var part in value.Split('\n', ','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) arr.Add(trimmed);
        }
        return arr;
    }

    private static JsonObject? ParseHeaders(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonNode.Parse(value) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string GenerateRealityPrivateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        ClampPrivateKey(bytes);
        return Base64Url(bytes);
    }

    private static string GenerateRealityShortId()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ClampPrivateKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("Reality keys must be 32 bytes.");
        }

        key[0] &= 248;
        key[31] &= 127;
        key[31] |= 64;
    }

}
