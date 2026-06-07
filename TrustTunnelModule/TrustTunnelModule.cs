using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.TrustTunnelModule;

public sealed class TrustTunnelModule : ICoreModule
{
    private const string BundleMarker = "# CitadelX-File:";

    public string Id => "TrustTunnel";
    public string Label => "TrustTunnel";
    public string? Description => "AdGuard TrustTunnel endpoint";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => TrustTunnelSimpleSetupSchema.Create();
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "{configPath}",
        UseRunCommand = false
    };

    public GitHubRepo? Repo => new() { Owner = "TrustTunnel", Repo = "TrustTunnel" };
    public string? NodeModuleAssemblyName => "CitadelX.TrustTunnelNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "trusttunnel", "tt", "adguard-vpn" };
    public string? IconKey => "trusttunnel";
    public RuntimeKind RuntimeKind => RuntimeKind.Process;
    public InstallDescriptor Install => new GitHubReleaseInstall
    {
        Repo = new GitHubRepo { Owner = "TrustTunnel", Repo = "TrustTunnel" },
        AssetRules = new AssetMatchRules
        {
            BinaryName = "trusttunnel_endpoint",
            NamePattern = "trusttunnel"
        }
    };

    public ConfigContract Config => new()
    {
        SupportsStructured = true,
        SupportsRaw = true,
        SchemaJson = SimpleSetupSchema?.SchemaJson,
        UiSchemaJson = SimpleSetupSchema?.UiSchemaJson,
        DefaultsJson = SimpleSetupSchema?.DefaultsJson,
        NativeFormat = NativeFormat.Text,
        EditorLanguage = "toml",
        SupportsUsers = true,
        UserIdentity = UserIdentityKind.Username,
        SupportsFlowEditor = false
    };

    public ConfigArtifact? BuildConfig(ConfigInput input, NodeContext node)
    {
        if (input.Mode == ConfigInputMode.Raw)
        {
            return new FileArtifact
            {
                FileName = "trusttunnel.bundle.conf",
                Content = Normalize(input.Raw ?? string.Empty),
                Format = NativeFormat.Text
            };
        }

        var values = input.Structured ?? new JsonObject();
        var listenAddress = GetString(values, "listenAddress", "0.0.0.0:443");
        var hostname = GetString(values, "hostname", "vpn.example.com");
        var certChainPath = GetString(values, "certChainPath", "certs/cert.pem");
        var privateKeyPath = GetString(values, "privateKeyPath", "certs/key.pem");
        var logLevel = GetString(values, "logLevel", "info");
        var publicAddress = GetString(values, "publicAddress", string.Empty);
        var dnsUpstreams = GetString(values, "dnsUpstreams", string.Empty);
        var ipv6Available = GetBool(values, "ipv6Available", true);
        var allowPrivate = GetBool(values, "allowPrivateNetworkConnections", false);
        var skipVerification = GetBool(values, "skipVerification", false);
        var pingEnable = GetBool(values, "pingEnable", false);
        var pingPath = GetString(values, "pingPath", "/ping");
        var speedtestEnable = GetBool(values, "speedtestEnable", false);
        var speedtestPath = GetString(values, "speedtestPath", "/speedtest");
        var authFailureStatusCode = GetInt(values, "authFailureStatusCode", 407);
        var forwardProtocol = GetString(values, "forwardProtocol", "direct");
        var socks5Address = GetString(values, "socks5Address", "127.0.0.1:1080");
        var socks5ExtendedAuth = GetBool(values, "socks5ExtendedAuth", false);
        var reverseProxyEnabled = GetBool(values, "reverseProxyEnabled", false);
        var reverseProxyServerAddress = GetString(values, "reverseProxyServerAddress", "127.0.0.1:8080");
        var reverseProxyPathMask = GetString(values, "reverseProxyPathMask", "/api");
        var reverseProxyHostname = GetString(values, "reverseProxyHostname", string.Empty);
        var reverseProxyH3BackwardCompatibility = GetBool(values, "reverseProxyH3BackwardCompatibility", false);
        var icmpEnabled = GetBool(values, "icmpEnabled", false);
        var icmpInterfaceName = GetString(values, "icmpInterfaceName", "eth0");
        var icmpRequestTimeoutSecs = GetInt(values, "icmpRequestTimeoutSecs", 3);
        var icmpRecvQueueCapacity = GetInt(values, "icmpRecvQueueCapacity", 256);
        var metricsEnabled = GetBool(values, "metricsEnabled", false);
        var metricsAddress = GetString(values, "metricsAddress", "127.0.0.1:1987");
        var metricsRequestTimeoutSecs = GetInt(values, "metricsRequestTimeoutSecs", 3);
        var denyCidrs = SplitMultiline(values, "denyCidrs");
        var allowCidrs = SplitMultiline(values, "allowCidrs");
        var rulesToml = GetString(values, "rulesToml", string.Empty);

        var vpn = $"""
        # CitadelX-LogLevel: {logLevel}
        # CitadelX-PublicAddress: {publicAddress}
        # CitadelX-ClientSkipVerification: {skipVerification.ToString().ToLowerInvariant()}
        # CitadelX-DnsUpstreams: {dnsUpstreams}
        listen_address = "{Toml(listenAddress)}"
        ipv6_available = {Bool(ipv6Available)}
        allow_private_network_connections = {Bool(allowPrivate)}
        auth_failure_status_code = {NormalizeAuthFailureStatusCode(authFailureStatusCode)}
        tls_handshake_timeout_secs = 10
        client_listener_timeout_secs = 600
        connection_establishment_timeout_secs = 30
        tcp_connections_timeout_secs = 604800
        udp_connections_timeout_secs = 300
        credentials_file = "credentials.toml"
        rules_file = "rules.toml"
        ping_enable = {Bool(pingEnable)}
        ping_path = "{Toml(pingPath)}"
        speedtest_enable = {Bool(speedtestEnable)}
        speedtest_path = "{Toml(speedtestPath)}"

        [listen_protocols]

        [listen_protocols.http1]
        upload_buffer_size = 32768

        [listen_protocols.http2]
        initial_connection_window_size = 8388608
        initial_stream_window_size = 131072
        max_concurrent_streams = 1000
        max_frame_size = 16384
        header_table_size = 65536

        [listen_protocols.quic]
        recv_udp_payload_size = 1350
        send_udp_payload_size = 1350
        initial_max_data = 104857600
        initial_max_stream_data_bidi_local = 1048576
        initial_max_stream_data_bidi_remote = 1048576
        initial_max_stream_data_uni = 1048576
        initial_max_streams_bidi = 4096
        initial_max_streams_uni = 4096
        max_connection_window = 25165824
        max_stream_window = 16777216
        disable_active_migration = true
        enable_early_data = true
        message_queue_capacity = 4096

        [forward_protocol]
        {BuildForwardProtocol(forwardProtocol, socks5Address, socks5ExtendedAuth)}
        {BuildIcmpSection(icmpEnabled, icmpInterfaceName, icmpRequestTimeoutSecs, icmpRecvQueueCapacity)}
        {BuildMetricsSection(metricsEnabled, metricsAddress, metricsRequestTimeoutSecs)}
        {BuildReverseProxySection(reverseProxyEnabled, reverseProxyServerAddress, reverseProxyPathMask, reverseProxyH3BackwardCompatibility)}
        """;

        var hosts = BuildHostsToml(
            hostname,
            certChainPath,
            privateKeyPath,
            reverseProxyEnabled ? reverseProxyHostname : string.Empty);
        var rules = BuildRulesToml(denyCidrs, allowCidrs, rulesToml);

        var bundle = BuildBundle(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vpn.toml"] = vpn,
            ["hosts.toml"] = hosts,
            ["credentials.toml"] = BuildEmptyCredentialsFile(),
            ["rules.toml"] = rules
        });

        return new FileArtifact
        {
            FileName = "trusttunnel.bundle.conf",
            Content = bundle,
            Format = NativeFormat.Text
        };
    }

    public string? BuildUserTemplate(UserTemplateRequest request)
    {
        JsonObject template;
        if (string.IsNullOrWhiteSpace(request.UserTemplateJson))
        {
            template = new JsonObject();
        }
        else
        {
            template = JsonNode.Parse(request.UserTemplateJson) as JsonObject ?? new JsonObject();
        }

        if (!HasString(template, "username"))
        {
            template["username"] = request.UserId;
        }

        if (!HasString(template, "password"))
        {
            template["password"] = GeneratePassword();
        }

        return template.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public override string ToString() => Id;

    public SubscriptionPayload BuildSubscription(SubscriptionRequest request)
    {
        var credentials = string.IsNullOrWhiteSpace(request.UserCredentialsJson)
            ? null
            : JsonNode.Parse(request.UserCredentialsJson) as JsonObject;
        var username = GetString(credentials, "username", request.UserId);
        var password = GetString(credentials, "password", string.Empty);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return SubscriptionPayload.UriList(Array.Empty<string>());
        }

        var files = ParseBundle(request.Config);
        var vpn = files.GetValueOrDefault("vpn.toml") ?? request.Config;
        var hosts = files.GetValueOrDefault("hosts.toml") ?? string.Empty;
        var hostname = ExtractTomlString(hosts, "hostname") ?? request.Host;
        var listenAddress = ExtractTomlString(vpn, "listen_address") ?? "0.0.0.0:443";
        var port = ExtractPort(listenAddress) ?? 443;
        var publicAddress = ExtractComment(vpn, "CitadelX-PublicAddress");
        var address = NormalizeAddress(string.IsNullOrWhiteSpace(publicAddress) ? request.Host : publicAddress, port);
        var skipVerification = string.Equals(ExtractComment(vpn, "CitadelX-ClientSkipVerification"), "true", StringComparison.OrdinalIgnoreCase);
        var dnsUpstreams = SplitCsv(ExtractComment(vpn, "CitadelX-DnsUpstreams"));

        var clientToml = BuildClientToml(hostname, address, username, password, skipVerification, dnsUpstreams);
        var deeplink = ComposeDeepLink(hostname, address, username, password, skipVerification, dnsUpstreams);
        return string.IsNullOrWhiteSpace(deeplink)
            ? SubscriptionPayload.ConfigFile($"{Sanitize(username)}.trusttunnel.toml", clientToml, "application/toml")
            : SubscriptionPayload.Combined(new[] { deeplink }, $"{Sanitize(username)}.trusttunnel.toml", clientToml, "application/toml");
    }

    private static string BuildBundle(IReadOnlyDictionary<string, string> files)
    {
        var builder = new StringBuilder();
        foreach (var (name, content) in files)
        {
            builder.Append(BundleMarker).Append(' ').Append(name).AppendLine();
            builder.Append(Normalize(content));
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static Dictionary<string, string> ParseBundle(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentName = string.Empty;
        var builder = new StringBuilder();
        foreach (var line in Normalize(content).Split('\n'))
        {
            if (line.StartsWith(BundleMarker, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(currentName))
                {
                    result[currentName] = builder.ToString().TrimEnd() + Environment.NewLine;
                }

                currentName = line[BundleMarker.Length..].Trim();
                builder.Clear();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(currentName))
            {
                builder.AppendLine(line);
            }
        }

        if (!string.IsNullOrWhiteSpace(currentName))
        {
            result[currentName] = builder.ToString().TrimEnd() + Environment.NewLine;
        }

        return result;
    }

    private static string BuildClientToml(string hostname, string address, string username, string password, bool skipVerification, IReadOnlyList<string> dnsUpstreams)
    {
        var dns = dnsUpstreams.Count == 0
            ? "[]"
            : "[" + string.Join(", ", dnsUpstreams.Select(item => $"\"{Toml(item)}\"")) + "]";
        return $"""
        hostname = "{Toml(hostname)}"
        addresses = ["{Toml(address)}"]
        custom_sni = ""
        has_ipv6 = true
        username = "{Toml(username)}"
        password = "{Toml(password)}"
        client_random_prefix = ""
        skip_verification = {Bool(skipVerification)}
        certificate = ""
        upstream_protocol = "http2"
        anti_dpi = false
        dns_upstreams = {dns}
        """;
    }

    private static string ComposeDeepLink(string hostname, string address, string username, string password, bool skipVerification, IReadOnlyList<string> dnsUpstreams)
    {
        var payload = new List<byte>();
        AddTlv(payload, 0x00, EncodeVarInt(1));
        AddTlv(payload, 0x01, Encoding.UTF8.GetBytes(hostname));
        AddTlv(payload, 0x05, Encoding.UTF8.GetBytes(username));
        AddTlv(payload, 0x06, Encoding.UTF8.GetBytes(password));
        AddTlv(payload, 0x02, Encoding.UTF8.GetBytes(address));
        if (skipVerification)
        {
            AddTlv(payload, 0x07, new byte[] { 0x01 });
        }

        if (dnsUpstreams.Count > 0)
        {
            var dns = new List<byte>();
            foreach (var upstream in dnsUpstreams)
            {
                var bytes = Encoding.UTF8.GetBytes(upstream);
                dns.AddRange(EncodeVarInt((ulong)bytes.Length));
                dns.AddRange(bytes);
            }

            AddTlv(payload, 0x0D, dns.ToArray());
        }

        var encoded = Convert.ToBase64String(payload.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"tt://?{encoded}";
    }

    private static void AddTlv(List<byte> target, ulong tag, byte[] value)
    {
        target.AddRange(EncodeVarInt(tag));
        target.AddRange(EncodeVarInt((ulong)value.Length));
        target.AddRange(value);
    }

    private static byte[] EncodeVarInt(ulong value)
    {
        if (value <= 0x3F)
        {
            return new[] { (byte)value };
        }

        if (value <= 0x3FFF)
        {
            var encoded = (ushort)(value | 0x4000);
            return new[] { (byte)(encoded >> 8), (byte)encoded };
        }

        if (value <= 0x3FFFFFFF)
        {
            var encoded = (uint)(value | 0x80000000);
            return new[] { (byte)(encoded >> 24), (byte)(encoded >> 16), (byte)(encoded >> 8), (byte)encoded };
        }

        var longEncoded = value | 0xC000000000000000;
        return BitConverter.GetBytes(longEncoded).Reverse().ToArray();
    }

    private static string? ExtractTomlString(string content, string key)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            return line[(eq + 1)..].Trim().Trim('"');
        }

        return null;
    }

    private static string? ExtractComment(string content, string key)
    {
        var prefix = $"# {key}:";
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static int? ExtractPort(string listenAddress)
    {
        var idx = listenAddress.LastIndexOf(':');
        return idx >= 0 && int.TryParse(listenAddress[(idx + 1)..], out var port) ? port : null;
    }

    private static string NormalizeAddress(string address, int port)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return $"127.0.0.1:{port}";
        }

        var value = address.Trim();
        if (value.StartsWith("[", StringComparison.Ordinal) || value.Count(ch => ch == ':') == 1)
        {
            return value;
        }

        return $"{value}:{port}";
    }

    private static IReadOnlyList<string> SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool HasString(JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out var node) && !string.IsNullOrWhiteSpace(node?.GetValue<string>());

    private static string GetString(JsonObject? obj, string key, string fallback)
        => obj is not null
           && obj.TryGetPropertyValue(key, out var node)
           && node is not null
           && node.GetValueKind() == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(node.GetValue<string>())
            ? node.GetValue<string>()
            : fallback;

    private static bool GetBool(JsonObject obj, string key, bool fallback)
        => obj.TryGetPropertyValue(key, out var node)
           && node is not null
           && node.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? node.GetValue<bool>()
            : fallback;

    private static int GetInt(JsonObject obj, string key, int fallback)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return fallback;
        }

        if (node.GetValueKind() == JsonValueKind.Number)
        {
            try
            {
                return node.GetValue<int>();
            }
            catch
            {
                return fallback;
            }
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.String when int.TryParse(node.GetValue<string>(), out var value) => value,
            _ => fallback
        };
    }

    private static int NormalizeAuthFailureStatusCode(int value)
        => value is 403 or 404 or 405 or 407 ? value : 407;

    private static IReadOnlyList<string> SplitMultiline(JsonObject obj, string key)
    {
        var raw = GetString(obj, key, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildForwardProtocol(string protocol, string socks5Address, bool socks5ExtendedAuth)
    {
        if (string.Equals(protocol, "socks5", StringComparison.OrdinalIgnoreCase))
        {
            return $"""
            [forward_protocol.socks5]
            address = "{Toml(socks5Address)}"
            extended_auth = {Bool(socks5ExtendedAuth)}
            """;
        }

        return """
        direct = {}
        """;
    }

    private static string BuildIcmpSection(bool enabled, string interfaceName, int requestTimeoutSecs, int recvQueueCapacity)
    {
        if (!enabled)
        {
            return string.Empty;
        }

        return $"""
        [icmp]
        interface_name = "{Toml(interfaceName)}"
        request_timeout_secs = {Math.Max(1, requestTimeoutSecs)}
        recv_message_queue_capacity = {Math.Max(1, recvQueueCapacity)}
        """;
    }

    private static string BuildMetricsSection(bool enabled, string address, int requestTimeoutSecs)
    {
        if (!enabled)
        {
            return string.Empty;
        }

        return $"""
        [metrics]
        address = "{Toml(address)}"
        request_timeout_secs = {Math.Max(1, requestTimeoutSecs)}
        """;
    }

    private static string BuildReverseProxySection(bool enabled, string serverAddress, string pathMask, bool h3BackwardCompatibility)
    {
        if (!enabled)
        {
            return string.Empty;
        }

        return $"""
        [reverse_proxy]
        server_address = "{Toml(serverAddress)}"
        path_mask = "{Toml(pathMask)}"
        h3_backward_compatibility = {Bool(h3BackwardCompatibility)}
        """;
    }

    private static string BuildHostsToml(string hostname, string certChainPath, string privateKeyPath, string reverseProxyHostname)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[[main_hosts]]");
        builder.Append("hostname = \"").Append(Toml(hostname)).AppendLine("\"");
        builder.Append("cert_chain_path = \"").Append(Toml(certChainPath)).AppendLine("\"");
        builder.Append("private_key_path = \"").Append(Toml(privateKeyPath)).AppendLine("\"");

        if (!string.IsNullOrWhiteSpace(reverseProxyHostname))
        {
            builder.AppendLine();
            builder.AppendLine("[[reverse_proxy_hosts]]");
            builder.Append("hostname = \"").Append(Toml(reverseProxyHostname)).AppendLine("\"");
            builder.Append("cert_chain_path = \"").Append(Toml(certChainPath)).AppendLine("\"");
            builder.Append("private_key_path = \"").Append(Toml(privateKeyPath)).AppendLine("\"");
        }

        return builder.ToString();
    }

    private static string BuildRulesToml(IReadOnlyList<string> denyCidrs, IReadOnlyList<string> allowCidrs, string extraRulesToml)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CitadelX-managed TrustTunnel rules.");
        builder.AppendLine("# If no rule matches, TrustTunnel allows the connection by default.");

        foreach (var cidr in denyCidrs)
        {
            builder.AppendLine();
            builder.AppendLine("[[rule]]");
            builder.AppendLine("action = \"deny\"");
            builder.Append("cidr = \"").Append(Toml(cidr)).AppendLine("\"");
        }

        foreach (var cidr in allowCidrs)
        {
            builder.AppendLine();
            builder.AppendLine("[[rule]]");
            builder.AppendLine("action = \"allow\"");
            builder.Append("cidr = \"").Append(Toml(cidr)).AppendLine("\"");
        }

        if (!string.IsNullOrWhiteSpace(extraRulesToml))
        {
            builder.AppendLine();
            builder.AppendLine("# Extra rules from Simple setup.");
            builder.AppendLine(Normalize(extraRulesToml).TrimEnd());
        }

        return builder.ToString();
    }

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string BuildEmptyCredentialsFile()
    {
        return $"""
        # CitadelX-managed clients. TrustTunnel requires at least one [[client]] entry.
        # This disabled placeholder is replaced when real users are attached.
        [[client]]
        username = "__citadelx_disabled__"
        password = "{GeneratePassword()}"
        """;
    }

    private static string Sanitize(string value)
    {
        var safe = string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return string.IsNullOrWhiteSpace(safe) ? "trusttunnel-client" : safe;
    }

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;

    private static string Toml(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Bool(bool value)
        => value ? "true" : "false";
}
