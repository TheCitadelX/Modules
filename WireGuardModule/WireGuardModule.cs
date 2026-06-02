using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.WireGuardModule;

public sealed class WireGuardModule : ICoreModule
{
    private const string ServerPrivateKeyToken = "${node.secret.wireguardPrivateKey}";

    public string Id => "WireGuard";
    public string Label => "WireGuard";
    public string? Description => "Kernel WireGuard via wg-quick";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => WireGuardSimpleSetupSchema.Schema;
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "",
        UseRunCommand = false
    };
    public GitHubRepo? Repo => null;
    public string? NodeModuleAssemblyName => "CitadelX.WireGuardNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "wireguard", "wg", "wg-quick" };
    public string? IconKey => "wireguard";
    public RuntimeKind RuntimeKind => RuntimeKind.SystemService;

    public CompatibilityDescriptor Compatibility => new()
    {
        SupportedOs = new[] { OsKind.Linux },
        RequiredFeatures = new[] { RequiredFeature.RootOrAdmin, RequiredFeature.NetAdmin, RequiredFeature.TunDevice, RequiredFeature.WireguardKernelModule }
    };

    public InstallDescriptor Install => new SystemPackageInstall
    {
        BinaryName = "wg-quick",
        PackageNames = new Dictionary<OsKind, string>
        {
            [OsKind.Linux] = "wireguard-tools"
        }
    };

    public ConfigContract Config => new()
    {
        SupportsStructured = true,
        SupportsRaw = true,
        NativeFormat = NativeFormat.Ini,
        SupportsUsers = true,
        UserIdentity = UserIdentityKind.WireguardPeer,
        SupportsFlowEditor = false,
        SchemaJson = WireGuardSimpleSetupSchema.Schema.SchemaJson,
        DefaultsJson = WireGuardSimpleSetupSchema.Schema.DefaultsJson
    };

    public ConfigArtifact BuildConfig(ConfigInput input, NodeContext node)
    {
        var structured = input.Mode == ConfigInputMode.Structured
            ? input.Structured ?? new JsonObject()
            : null;

        var content = input.Mode == ConfigInputMode.Raw
            ? input.Raw ?? string.Empty
            : BuildWireGuardConfig(structured ?? new JsonObject());

        var interfaceName = GetString(structured, "interfaceName") ?? "wg0";
        return new FileArtifact
        {
            FileName = $"{SanitizeInterfaceName(interfaceName)}.conf",
            Content = content,
            Format = NativeFormat.Ini,
            Placeholders = new[]
            {
                new PlaceholderDirective
                {
                    Token = ServerPrivateKeyToken,
                    Kind = PlaceholderKind.Secret,
                    Generator = "wireguard-private-key"
                }
            }
        };
    }

    public string? BuildUserTemplate(UserTemplateRequest request)
    {
        var template = ParseObject(request.UserTemplateJson) ?? new JsonObject();
        if (!HasString(template, "privateKey") || !HasString(template, "publicKey"))
        {
            var privateKey = GenerateWireGuardPrivateKey();
            template["privateKey"] = privateKey;
            template["publicKey"] = DeriveWireGuardPublicKey(privateKey);
        }

        if (!HasString(template, "presharedKey"))
        {
            template["presharedKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        return template.ToJsonString();
    }

    public SubscriptionPayload BuildSubscription(SubscriptionRequest request)
    {
        var clientUri = BuildClientUri(request);
        var clientConfig = BuildClientConfig(request);
        if (!string.IsNullOrWhiteSpace(clientConfig))
        {
            return SubscriptionPayload.Combined(
                string.IsNullOrWhiteSpace(clientUri) ? Array.Empty<string>() : new[] { clientUri },
                $"{SafeFileName(request.UserId)}.conf",
                clientConfig);
        }

        return string.IsNullOrWhiteSpace(clientUri)
            ? SubscriptionPayload.UriList(Array.Empty<string>())
            : SubscriptionPayload.UriList(new[] { clientUri });
    }

    public ConfigArtifact ApplyNodeReport(ConfigArtifact artifact, JsonElement result)
    {
        var serverPublicKey = ExtractServerPublicKey(result);
        if (string.IsNullOrWhiteSpace(serverPublicKey) || artifact is not FileArtifact file)
        {
            return artifact;
        }

        return new FileArtifact
        {
            SchemaVersion = file.SchemaVersion,
            Placeholders = file.Placeholders,
            FileName = file.FileName,
            Content = UpsertServerPublicKey(file.Content, serverPublicKey),
            Format = file.Format
        };
    }

    private static string BuildWireGuardConfig(JsonObject input)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {ServerPrivateKeyToken}");
        AddLine(sb, "Address", GetString(input, "interfaceAddress") ?? "10.77.0.1/24");
        AddLine(sb, "ListenPort", GetString(input, "listenPort") ?? "51820");
        AddLine(sb, "MTU", GetString(input, "mtu"));
        AddLine(sb, "Table", GetString(input, "table"));
        AddLine(sb, "PostUp", GetString(input, "postUp"));
        AddLine(sb, "PostDown", GetString(input, "postDown"));

        var clientDns = GetString(input, "dns");
        if (!string.IsNullOrWhiteSpace(clientDns))
        {
            sb.AppendLine($"# CitadelX-ClientDNS = {clientDns}");
        }

        var serverPublicKey = GetString(input, "serverPublicKey");
        if (!string.IsNullOrWhiteSpace(serverPublicKey))
        {
            sb.AppendLine($"# CitadelX-ServerPublicKey = {serverPublicKey}");
        }

        return sb.ToString();
    }

    private static string? BuildClientConfig(SubscriptionRequest request)
    {
        var credentials = ParseObject(request.UserCredentialsJson);
        var resources = ParseObject(request.ResourceAllocationJson);
        if (credentials is null || resources is null)
        {
            return null;
        }

        var privateKey = GetString(credentials, "privateKey");
        var serverPublicKey = ResolveServerPublicKey(request.Config);
        var address = GetString(resources, "peerAddress");
        var endpoint = ResolveEndpoint(request);
        if (string.IsNullOrWhiteSpace(privateKey)
            || string.IsNullOrWhiteSpace(serverPublicKey)
            || string.IsNullOrWhiteSpace(address)
            || string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var allowedIps = GetString(credentials, "allowedIps") ?? ResolveConfigValue(request.Config, "AllowedIPs") ?? "0.0.0.0/0, ::/0";
        var dns = ResolveClientDns(request.Config) ?? ResolveConfigValue(request.Config, "DNS");
        var mtu = ResolveConfigValue(request.Config, "MTU");
        var persistentKeepalive = GetString(credentials, "persistentKeepalive") ?? "25";
        var presharedKey = GetString(credentials, "presharedKey");

        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        AddLine(sb, "PrivateKey", privateKey);
        AddLine(sb, "Address", address);
        AddLine(sb, "DNS", dns);
        AddLine(sb, "MTU", mtu);
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        AddLine(sb, "PublicKey", serverPublicKey);
        AddLine(sb, "PresharedKey", presharedKey);
        AddLine(sb, "AllowedIPs", allowedIps);
        AddLine(sb, "Endpoint", endpoint);
        AddLine(sb, "PersistentKeepalive", persistentKeepalive);
        return sb.ToString();
    }

    private static string? BuildClientUri(SubscriptionRequest request)
    {
        var credentials = ParseObject(request.UserCredentialsJson);
        var resources = ParseObject(request.ResourceAllocationJson);
        if (credentials is null || resources is null)
        {
            return null;
        }

        var privateKey = GetString(credentials, "privateKey");
        var serverPublicKey = ResolveServerPublicKey(request.Config);
        var address = GetString(resources, "peerAddress");
        var port = ResolveConfigValue(request.Config, "ListenPort");
        if (string.IsNullOrWhiteSpace(privateKey)
            || string.IsNullOrWhiteSpace(serverPublicKey)
            || string.IsNullOrWhiteSpace(address)
            || string.IsNullOrWhiteSpace(request.Host)
            || string.IsNullOrWhiteSpace(port))
        {
            return null;
        }

        var query = new List<string>();
        AddQueryValue(query, "private_key", privateKey);
        AddQueryValue(query, "local_address", NormalizeLocalAddress(address));
        AddQueryValue(query, "mtu", ResolveConfigValue(request.Config, "MTU"));
        AddAmneziaQueryValues(query, credentials, request.Config);
        AddQueryValue(query, "public_key", serverPublicKey);
        AddQueryValue(query, "pre_shared_key", GetString(credentials, "presharedKey"));
        AddQueryValue(query, "reserved", FirstString(credentials, "reserved", "reservedBytes"));
        AddQueryValue(query, "persistent_keepalive_interval", GetString(credentials, "persistentKeepalive") ?? "25");

        return $"wg://{FormatUriHost(request.Host)}:{Escape(port)}?{string.Join("&", query)}#{Escape(request.Label)}";
    }

    private static void AddAmneziaQueryValues(List<string> query, JsonObject credentials, string config)
    {
        var amneziaValues = new (string ShortKey, string LongKey, string ConfigKey, string[] CredentialKeys)[]
        {
            ("jc", "junk_packet_count", "Jc", new[] { "jc", "junk_packet_count", "junkPacketCount" }),
            ("jmin", "junk_packet_min_size", "Jmin", new[] { "jmin", "junk_packet_min_size", "junkPacketMinSize" }),
            ("jmax", "junk_packet_max_size", "Jmax", new[] { "jmax", "junk_packet_max_size", "junkPacketMaxSize" }),
            ("s1", "init_packet_junk_size", "S1", new[] { "s1", "init_packet_junk_size", "initPacketJunkSize" }),
            ("s2", "response_packet_junk_size", "S2", new[] { "s2", "response_packet_junk_size", "responsePacketJunkSize" }),
            ("s3", "underload_packet_junk_size", "S3", new[] { "s3", "underload_packet_junk_size", "underloadPacketJunkSize" }),
            ("s4", "transport_packet_junk_size", "S4", new[] { "s4", "transport_packet_junk_size", "transportPacketJunkSize" }),
            ("h1", "init_packet_magic_header", "H1", new[] { "h1", "init_packet_magic_header", "initPacketMagicHeader" }),
            ("h2", "response_packet_magic_header", "H2", new[] { "h2", "response_packet_magic_header", "responsePacketMagicHeader" }),
            ("h3", "underload_packet_magic_header", "H3", new[] { "h3", "underload_packet_magic_header", "underloadPacketMagicHeader" }),
            ("h4", "transport_packet_magic_header", "H4", new[] { "h4", "transport_packet_magic_header", "transportPacketMagicHeader" }),
            ("i1", "i1", "I1", new[] { "i1" }),
            ("i2", "i2", "I2", new[] { "i2" }),
            ("i3", "i3", "I3", new[] { "i3" }),
            ("i4", "i4", "I4", new[] { "i4" }),
            ("i5", "i5", "I5", new[] { "i5" })
        };

        var startIndex = query.Count;
        foreach (var mapping in amneziaValues)
        {
            var value = FirstNonEmpty(FirstString(credentials, mapping.CredentialKeys), ResolveConfigValue(config, mapping.ConfigKey));
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            AddQueryValue(query, mapping.ShortKey, value);
            if (!string.Equals(mapping.ShortKey, mapping.LongKey, StringComparison.OrdinalIgnoreCase))
            {
                AddQueryValue(query, mapping.LongKey, value);
            }
        }

        var enabled = IsTrue(FirstString(credentials, "enable_amnezia", "enableAmnezia"))
                      || query.Count > startIndex;
        if (enabled)
        {
            query.Insert(startIndex, "enable_amnezia=true");
        }
    }

    private static string? ResolveEndpoint(SubscriptionRequest request)
    {
        var port = ResolveConfigValue(request.Config, "ListenPort");
        return string.IsNullOrWhiteSpace(port) ? null : $"{request.Host}:{port}";
    }

    private static string? ResolveServerPublicKey(string config)
    {
        foreach (var line in ReadLines(config))
        {
            var trimmed = line.Trim();
            const string marker = "# CitadelX-ServerPublicKey =";
            if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[marker.Length..].Trim();
            }
        }

        return null;
    }

    private static string? ResolveClientDns(string config)
    {
        foreach (var line in ReadLines(config))
        {
            var trimmed = line.Trim();
            const string marker = "# CitadelX-ClientDNS =";
            if (trimmed.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[marker.Length..].Trim();
            }
        }

        return null;
    }

    private static string? ExtractServerPublicKey(JsonElement result)
    {
        if (!result.TryGetProperty("placeholderReports", out var reports) || reports.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var report in reports.EnumerateArray())
        {
            var generator = report.TryGetProperty("generator", out var generatorProperty)
                ? generatorProperty.GetString()
                : null;
            if (!string.Equals(generator, "wireguard-private-key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (report.TryGetProperty("publicKey", out var publicKeyProperty))
            {
                return publicKeyProperty.GetString();
            }
        }

        return null;
    }

    private static string UpsertServerPublicKey(string config, string publicKey)
    {
        const string marker = "# CitadelX-ServerPublicKey =";
        var lines = ReadLines(config).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{marker} {publicKey}";
                return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
            }
        }

        var insertAt = lines.FindLastIndex(line => line.Trim().StartsWith("[Interface]", StringComparison.OrdinalIgnoreCase));
        if (insertAt < 0)
        {
            lines.Insert(0, $"{marker} {publicKey}");
        }
        else
        {
            var nextSection = lines.FindIndex(insertAt + 1, line => line.Trim().StartsWith("[", StringComparison.Ordinal));
            lines.Insert(nextSection < 0 ? lines.Count : nextSection, $"{marker} {publicKey}");
        }

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static string? ResolveConfigValue(string config, string key)
    {
        foreach (var line in ReadLines(config))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return null;
    }

    private static IEnumerable<string> ReadLines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static void AddLine(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"{key} = {value}");
        }
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

    private static bool HasString(JsonObject obj, string key)
        => !string.IsNullOrWhiteSpace(GetString(obj, key));

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

    private static string? FirstString(JsonObject? obj, params string[] keys)
        => keys.Select(key => GetString(obj, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool IsTrue(string? value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLocalAddress(string value)
        => string.Join("-", value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string FormatUriHost(string host)
        => host.Contains(':', StringComparison.Ordinal) && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;

    private static void AddQueryValue(List<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{key}={Escape(value)}");
        }
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value);

    private static string GenerateWireGuardPrivateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        ClampPrivateKey(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string DeriveWireGuardPublicKey(string privateKey)
    {
        var scalar = Convert.FromBase64String(privateKey);
        ClampPrivateKey(scalar);
        var point = X25519(scalar);
        return Convert.ToBase64String(point);
    }

    private static void ClampPrivateKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("WireGuard keys must be 32 bytes.");
        }

        key[0] &= 248;
        key[31] &= 127;
        key[31] |= 64;
    }

    private static byte[] X25519(byte[] scalar)
    {
        var p = (BigInteger.One << 255) - 19;
        var x1 = new BigInteger(new byte[] { 9 }, isUnsigned: true, isBigEndian: false);
        var x2 = BigInteger.One;
        var z2 = BigInteger.Zero;
        var x3 = x1;
        var z3 = BigInteger.One;
        var swap = 0;

        for (var t = 254; t >= 0; t--)
        {
            var kt = (scalar[t / 8] >> (t & 7)) & 1;
            swap ^= kt;
            ConditionalSwap(swap, ref x2, ref x3);
            ConditionalSwap(swap, ref z2, ref z3);
            swap = kt;

            var a = Mod(x2 + z2, p);
            var aa = Mod(a * a, p);
            var b = Mod(x2 - z2, p);
            var bb = Mod(b * b, p);
            var e = Mod(aa - bb, p);
            var c = Mod(x3 + z3, p);
            var d = Mod(x3 - z3, p);
            var da = Mod(d * a, p);
            var cb = Mod(c * b, p);
            x3 = Mod((da + cb) * (da + cb), p);
            z3 = Mod(x1 * Mod((da - cb) * (da - cb), p), p);
            x2 = Mod(aa * bb, p);
            z2 = Mod(e * (aa + 121665 * e), p);
        }

        ConditionalSwap(swap, ref x2, ref x3);
        ConditionalSwap(swap, ref z2, ref z3);
        var result = Mod(x2 * BigInteger.ModPow(z2, p - 2, p), p);
        var bytes = result.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Resize(ref bytes, 32);
        return bytes;
    }

    private static void ConditionalSwap(int swap, ref BigInteger a, ref BigInteger b)
    {
        if (swap == 0)
        {
            return;
        }

        (a, b) = (b, a);
    }

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var result = value % modulus;
        return result.Sign < 0 ? result + modulus : result;
    }

    private static string SanitizeInterfaceName(string value)
    {
        var sanitized = string.Concat(value.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return string.IsNullOrWhiteSpace(sanitized) ? "wg0" : sanitized;
    }

    private static string SafeFileName(string value)
    {
        var safe = string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        return string.IsNullOrWhiteSpace(safe) ? "wireguard-client" : safe;
    }
}
