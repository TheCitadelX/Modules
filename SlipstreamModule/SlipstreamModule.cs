using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.SlipstreamModule;

public sealed class SlipstreamModule : ICoreModule
{
    public string Id => "Slipstream";
    public string Label => "Slipstream";
    public string? Description => "High-performance QUIC-over-DNS tunnel using slipstream-rust";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => SlipstreamSimpleSetupSchema.Create();
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "",
        UseRunCommand = false
    };

    public GitHubRepo? Repo => new()
    {
        Owner = "Mygod",
        Repo = "slipstream-rust"
    };

    public string? NodeModuleAssemblyName => "CitadelX.SlipstreamNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "slipstream", "slipstream-rust", "quic-dns", "dns-quic-tunnel" };
    public string? IconKey => "slipstream";
    public RuntimeKind RuntimeKind => RuntimeKind.Process;

    public InstallDescriptor Install => new SystemPackageInstall
    {
        BinaryName = "slipstream-server",
        PackageNames = new Dictionary<OsKind, string>
        {
            [OsKind.Linux] = "git"
        },
        PackageNamesByManager = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["apt-get"] = new[] { "git", "ca-certificates", "curl", "build-essential", "pkg-config", "libssl-dev", "cmake", "python3" },
            ["dnf"] = new[] { "git", "ca-certificates", "curl", "gcc", "gcc-c++", "pkgconf-pkg-config", "openssl-devel", "cmake", "python3" },
            ["yum"] = new[] { "git", "ca-certificates", "curl", "gcc", "gcc-c++", "pkgconfig", "openssl-devel", "cmake", "python3" },
            ["pacman"] = new[] { "git", "ca-certificates", "curl", "base-devel", "pkgconf", "openssl", "cmake", "python" },
            ["zypper"] = new[] { "git", "ca-certificates", "curl", "gcc", "gcc-c++", "pkg-config", "libopenssl-devel", "cmake", "python3" }
        },
        PostInstallValidationSteps = new[]
        {
            new SystemPackageValidationStep
            {
                Description = "Build and install slipstream-rust client/server from upstream source",
                Shell = """
                set -e
                export HOME="${HOME:-/root}"
                tools_dir="${CITADELX_TOOLS_DIR:-/opt/citadelx/tools}"
                src_dir="$tools_dir/slipstream-rust"
                cargo_home="${CARGO_HOME:-$tools_dir/cargo}"
                rustup_home="${RUSTUP_HOME:-$tools_dir/rustup}"
                export CARGO_HOME="$cargo_home"
                export RUSTUP_HOME="$rustup_home"
                export PATH="$cargo_home/bin:$PATH"
                mkdir -p "$tools_dir" "$cargo_home" "$rustup_home"

                if ! command -v cargo >/dev/null 2>&1; then
                  curl --proto '=https' --tlsv1.2 -fsSL https://sh.rustup.rs -o "$tools_dir/rustup-init.sh"
                  sh "$tools_dir/rustup-init.sh" -y --no-modify-path --profile minimal --default-toolchain stable
                fi

                if [ ! -d "$src_dir/.git" ]; then
                  rm -rf "$src_dir"
                  git clone --recursive https://github.com/Mygod/slipstream-rust.git "$src_dir"
                else
                  git -C "$src_dir" fetch --tags --force origin
                  git -C "$src_dir" checkout origin/main
                  git -C "$src_dir" submodule update --init --recursive
                fi

                cd "$src_dir"
                cargo build --release -p slipstream-client -p slipstream-server
                install -m 0755 target/release/slipstream-server /usr/local/bin/slipstream-server
                install -m 0755 target/release/slipstream-client /usr/local/bin/slipstream-client
                command -v slipstream-server >/dev/null
                command -v slipstream-client >/dev/null
                slipstream-server --help >/dev/null
                slipstream-client --help >/dev/null
                """
            }
        },
        UninstallSteps = new[]
        {
            new SystemPackageUninstallStep
            {
                Description = "Remove Slipstream binaries installed by CitadelX",
                Shell = "rm -f /usr/local/bin/slipstream-server /usr/local/bin/slipstream-client"
            }
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
        EditorLanguage = "properties",
        SupportsUsers = true,
        UserIdentity = UserIdentityKind.None,
        SupportsFlowEditor = false
    };

    public ConfigArtifact BuildConfig(ConfigInput input, NodeContext node)
    {
        var content = input.Mode == ConfigInputMode.Raw
            ? Normalize(input.Raw ?? string.Empty)
            : BuildKeyValueConfig(input.Structured ?? new JsonObject());

        return new FileArtifact
        {
            FileName = "slipstream.conf",
            Content = content,
            Format = NativeFormat.Text
        };
    }

    public string? BuildUserTemplate(UserTemplateRequest request)
        => string.IsNullOrWhiteSpace(request.UserTemplateJson) ? "{}" : request.UserTemplateJson;

    public SubscriptionPayload BuildSubscription(SubscriptionRequest request)
    {
        var values = ParseKeyValues(request.Config);
        var domain = Get(values, "domain", "slip.example.com");
        var localListen = Get(values, "clientLocalListen", "127.0.0.1:1080");
        var (localHost, localPort) = SplitHostPort(localListen, "127.0.0.1", 1080);
        var recursiveResolvers = SplitList(Get(values, "clientResolvers", "1.1.1.1:53"));
        var authoritativeResolvers = SplitList(Get(values, "clientAuthoritativeResolvers", string.Empty));
        var congestion = Get(values, "clientCongestionControl", string.Empty);
        var keepAlive = Get(values, "clientKeepAliveMs", "400");
        var forwardMode = NormalizeForwardMode(Get(values, "forwardMode", "socks5Sidecar"));
        var sidecarListen = Get(values, "sidecarListen", "127.0.0.1:10818");
        var targetAddress = Get(values, "targetAddress", "127.0.0.1:22");
        var effectiveTarget = forwardMode == "socks5Sidecar" ? sidecarListen : targetAddress;
        var nameServerHost = Get(values, "nameServerHost", string.Empty);
        var nameServerAddress = Get(values, "nameServerAddress", string.Empty);
        var clientArgs = BuildClientArgs(localHost, localPort, recursiveResolvers, authoritativeResolvers, domain, congestion, keepAlive);

        var file = $"""
        # CitadelX Slipstream client profile
        # Slipstream tunnels TCP through DNS by carrying QUIC packets in DNS queries/responses.
        # Start this local client, then point your application to {localHost}:{localPort}.
        #
        # Server forwards incoming tunnel TCP to: {effectiveTarget}
        # Required DNS delegation:
        #   {domain} NS {Fallback(nameServerHost, "<your-nameserver-host>")}
        #   {Fallback(nameServerHost, "<your-nameserver-host>")} A {Fallback(nameServerAddress, "<your-node-public-ip>")}
        #
        # Client command:
        slipstream-client {clientArgs}

        # Quick proxy test when the remote sidecar is mixed/SOCKS:
        #   curl --proxy socks5h://{localHost}:{localPort} https://api.ipify.org
        #
        # For strict cert pinning, download/copy the server cert and add:
        #   --cert ./cert.pem
        """;

        return SubscriptionPayload.ConfigFile(
            $"{Sanitize(request.UserId)}.slipstream.txt",
            Normalize(file),
            "text/plain");
    }

    private static string BuildKeyValueConfig(JsonObject input)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CitadelX-Slipstream");
        builder.AppendLine("# DNS setup example: slip.example.com NS ns-slip.example.com; ns-slip.example.com A <node-public-ip>");
        Add(builder, "domain", GetString(input, "domain", "slip.example.com"));
        Add(builder, "udpListen", GetString(input, "udpListen", ":53"));
        Add(builder, "forwardMode", NormalizeForwardMode(GetString(input, "forwardMode", "socks5Sidecar")));
        Add(builder, "targetAddress", GetString(input, "targetAddress", "127.0.0.1:22"));
        Add(builder, "clientLocalListen", GetString(input, "clientLocalListen", "127.0.0.1:1080"));
        Add(builder, "sidecarListen", GetString(input, "sidecarListen", "127.0.0.1:10818"));
        Add(builder, "sidecarInboundType", NormalizeSidecarInboundType(GetString(input, "sidecarInboundType", "mixed")));
        Add(builder, "sidecarOutbound", NormalizeSidecarOutbound(GetString(input, "sidecarOutbound", "direct")));
        Add(builder, "sidecarAuthEnabled", GetBool(input, "sidecarAuthEnabled", false) ? "true" : "false");
        Add(builder, "sidecarUsername", GetString(input, "sidecarUsername", "slipstream"));
        Add(builder, "sidecarPassword", GetString(input, "sidecarPassword", string.Empty));
        Add(builder, "clientResolvers", MultiLineToCsv(GetString(input, "clientResolvers", "1.1.1.1:53\n8.8.8.8:53")));
        Add(builder, "clientAuthoritativeResolvers", MultiLineToCsv(GetString(input, "clientAuthoritativeResolvers", string.Empty)));
        Add(builder, "clientCongestionControl", NormalizeCongestion(GetString(input, "clientCongestionControl", string.Empty)));
        Add(builder, "clientKeepAliveMs", GetNumber(input, "clientKeepAliveMs", 400).ToString());
        Add(builder, "nameServerHost", GetString(input, "nameServerHost", "ns-slip.example.com"));
        Add(builder, "nameServerAddress", GetString(input, "nameServerAddress", string.Empty));
        Add(builder, "certPath", GetString(input, "certPath", string.Empty));
        Add(builder, "keyPath", GetString(input, "keyPath", string.Empty));
        Add(builder, "resetSeedPath", GetString(input, "resetSeedPath", string.Empty));
        Add(builder, "maxConnections", GetNumber(input, "maxConnections", 256).ToString());
        Add(builder, "idleTimeoutSeconds", GetNumber(input, "idleTimeoutSeconds", 60).ToString());
        Add(builder, "fallbackUdp", GetString(input, "fallbackUdp", string.Empty));
        Add(builder, "sidecarBinaryPath", GetString(input, "sidecarBinaryPath", string.Empty));
        Add(builder, "sidecarLogLevel", NormalizeLogLevel(GetString(input, "sidecarLogLevel", "info")));
        Add(builder, "notes", SingleLine(GetString(input, "notes", string.Empty)));
        return builder.ToString();
    }

    private static string BuildClientArgs(
        string localHost,
        int localPort,
        IReadOnlyList<string> recursiveResolvers,
        IReadOnlyList<string> authoritativeResolvers,
        string domain,
        string congestion,
        string keepAlive)
    {
        var args = new List<string>
        {
            "--tcp-listen-host",
            Shell(localHost),
            "--tcp-listen-port",
            localPort.ToString()
        };

        foreach (var resolver in recursiveResolvers)
        {
            args.Add("--resolver");
            args.Add(Shell(resolver));
        }

        foreach (var resolver in authoritativeResolvers)
        {
            args.Add("--authoritative");
            args.Add(Shell(resolver));
        }

        if (!string.IsNullOrWhiteSpace(congestion))
        {
            args.Add("--congestion-control");
            args.Add(Shell(congestion));
        }

        if (!string.IsNullOrWhiteSpace(keepAlive))
        {
            args.Add("--keep-alive-interval");
            args.Add(Shell(keepAlive));
        }

        args.Add("--domain");
        args.Add(Shell(domain));
        return string.Join(' ', args);
    }

    private static Dictionary<string, string> ParseKeyValues(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Normalize(content).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                result[parts[0]] = Unquote(parts[1]);
            }
        }

        return result;
    }

    private static void Add(StringBuilder builder, string key, string value)
        => builder.Append(key).Append(" = ").AppendLine(EscapeValue(value));

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string GetString(JsonObject obj, string key, string fallback)
        => obj.TryGetPropertyValue(key, out var node)
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

    private static int GetNumber(JsonObject obj, string key, int fallback)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        {
            return fallback;
        }

        if (node.GetValueKind() != JsonValueKind.Number)
        {
            return fallback;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            return fallback;
        }
    }

    private static string MultiLineToCsv(string value)
        => string.Join(",", SplitList(value));

    private static IReadOnlyList<string> SplitList(string value)
        => value
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static (string Host, int Port) SplitHostPort(string value, string fallbackHost, int fallbackPort)
    {
        var trimmed = value.Trim();
        var index = trimmed.LastIndexOf(':');
        if (index < 0)
        {
            return (fallbackHost, int.TryParse(trimmed, out var port) ? port : fallbackPort);
        }

        var host = trimmed[..index];
        return (string.IsNullOrWhiteSpace(host) ? fallbackHost : host, int.TryParse(trimmed[(index + 1)..], out var parsed) ? parsed : fallbackPort);
    }

    private static string NormalizeForwardMode(string value)
        => value.Equals("rawTcp", StringComparison.OrdinalIgnoreCase) ? "rawTcp" : "socks5Sidecar";

    private static string NormalizeSidecarInboundType(string value)
        => value.Equals("socks", StringComparison.OrdinalIgnoreCase) ? "socks" : "mixed";

    private static string NormalizeSidecarOutbound(string value)
        => value.Equals("block", StringComparison.OrdinalIgnoreCase) ? "block" : "direct";

    private static string NormalizeLogLevel(string value)
        => value.Equals("debug", StringComparison.OrdinalIgnoreCase)
            ? "debug"
            : value.Equals("warn", StringComparison.OrdinalIgnoreCase)
                ? "warn"
                : value.Equals("error", StringComparison.OrdinalIgnoreCase)
                    ? "error"
                    : "info";

    private static string NormalizeCongestion(string value)
        => value.Equals("bbr", StringComparison.OrdinalIgnoreCase)
            ? "bbr"
            : value.Equals("dcubic", StringComparison.OrdinalIgnoreCase)
                ? "dcubic"
                : string.Empty;

    private static string SingleLine(string value)
        => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private static string EscapeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"'
            ? trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
            : trimmed;
    }

    private static string Shell(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "''"
            : "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string Fallback(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Sanitize(string value)
    {
        var safe = string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return string.IsNullOrWhiteSpace(safe) ? "slipstream-client" : safe;
    }

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;
}
