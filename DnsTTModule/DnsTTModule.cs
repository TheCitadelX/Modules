using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Backend.Cores;
using CitadelX.Backend.Options;
using CitadelX.Modules.Abstractions;

namespace CitadelX.DnsTTModule;

public sealed class DnsTTModule : ICoreModule
{
    public string Id => "DnsTT";
    public string Label => "DnsTT";
    public string? Description => "TCP tunnel over DNS using dnstt-server";
    public bool Ready => true;
    public bool SupportsAutoInstall => true;
    public bool SupportsSimpleSetup => true;
    public CoreConfigSchema? SimpleSetupSchema => DnsTTSimpleSetupSchema.Create();
    public CoreLaunchProfile? LaunchProfile => new()
    {
        ArgumentsTemplate = "",
        UseRunCommand = false
    };

    public GitHubRepo? Repo => null;
    public string? NodeModuleAssemblyName => "CitadelX.DnsTTNodeModule.dll";
    public IReadOnlyList<string> Aliases => new[] { "dnstt", "dns-tunnel", "dns-over-dns" };
    public string? IconKey => "dnstt";
    public RuntimeKind RuntimeKind => RuntimeKind.Process;

    public InstallDescriptor Install => new SystemPackageInstall
    {
        BinaryName = "dnstt-server",
        PackageNames = new Dictionary<OsKind, string>
        {
            [OsKind.Linux] = "git"
        },
        PackageNamesByManager = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["apt-get"] = new[] { "git", "ca-certificates", "curl", "tar" },
            ["dnf"] = new[] { "git", "ca-certificates", "curl", "tar" },
            ["yum"] = new[] { "git", "ca-certificates", "curl", "tar" },
            ["pacman"] = new[] { "git", "ca-certificates", "curl", "tar" },
            ["zypper"] = new[] { "git", "ca-certificates", "curl", "tar" }
        },
        PostInstallValidationSteps = new[]
        {
            new SystemPackageValidationStep
            {
                Description = "Build and install DnsTT from official source",
                Shell = """
                set -e
                export HOME="${HOME:-/root}"
                tools_dir="${CITADELX_TOOLS_DIR:-/opt/citadelx/tools}"
                export XDG_CACHE_HOME="${XDG_CACHE_HOME:-$tools_dir/cache}"
                export GOCACHE="${GOCACHE:-$tools_dir/go-cache/build}"
                export GOPATH="${GOPATH:-$tools_dir/go-cache/path}"
                mkdir -p "$GOCACHE" "$GOPATH"
                if ! command -v dnstt-server >/dev/null 2>&1 || ! command -v dnstt-client >/dev/null 2>&1; then
                  tmp="$(mktemp -d)"
                  trap 'rm -rf "$tmp"' EXIT

                  go_cmd=""
                  if command -v go >/dev/null 2>&1; then
                    go_version_text="$(go version 2>/dev/null || true)"
                    go_major="$(printf '%s' "$go_version_text" | sed -n 's/.* go\([0-9][0-9]*\)\.\([0-9][0-9]*\).*/\1/p')"
                    go_minor="$(printf '%s' "$go_version_text" | sed -n 's/.* go\([0-9][0-9]*\)\.\([0-9][0-9]*\).*/\2/p')"
                    if [ -n "$go_major" ] && [ -n "$go_minor" ]; then
                      if [ "$go_major" -gt 1 ] || { [ "$go_major" -eq 1 ] && [ "$go_minor" -ge 20 ]; }; then
                        go_cmd="$(command -v go)"
                      fi
                    fi
                  fi

                  if [ -z "$go_cmd" ]; then
                    go_version="${CITADELX_GO_VERSION:-1.26.4}"
                    case "$(uname -m)" in
                      x86_64|amd64) go_arch="amd64" ;;
                      aarch64|arm64) go_arch="arm64" ;;
                      i386|i686) go_arch="386" ;;
                      armv6l|armv7l) go_arch="armv6l" ;;
                      *) echo "Unsupported Go architecture: $(uname -m)" >&2; exit 1 ;;
                    esac

                    go_root="$tools_dir/go-$go_version"
                    if [ ! -x "$go_root/bin/go" ]; then
                      rm -rf "$go_root.tmp"
                      mkdir -p "$go_root.tmp"
                      curl -fsSL "https://go.dev/dl/go${go_version}.linux-${go_arch}.tar.gz" -o "$tmp/go.tgz"
                      tar -xzf "$tmp/go.tgz" -C "$go_root.tmp" --strip-components=1
                      rm -rf "$go_root"
                      mv "$go_root.tmp" "$go_root"
                    fi
                    go_cmd="$go_root/bin/go"
                  fi

                  git clone https://www.bamsoftware.com/git/dnstt.git "$tmp/dnstt"
                  cd "$tmp/dnstt"
                  "$go_cmd" build -o "$tmp/dnstt-server-bin" ./dnstt-server
                  "$go_cmd" build -o "$tmp/dnstt-client-bin" ./dnstt-client
                  install -m 0755 "$tmp/dnstt-server-bin" /usr/local/bin/dnstt-server
                  install -m 0755 "$tmp/dnstt-client-bin" /usr/local/bin/dnstt-client
                fi
                command -v dnstt-server >/dev/null
                command -v dnstt-client >/dev/null
                dnstt-server -h >/dev/null 2>&1 || true
                dnstt-client -h >/dev/null 2>&1 || true
                """
            }
        },
        UninstallSteps = new[]
        {
            new SystemPackageUninstallStep
            {
                Description = "Remove DnsTT binaries installed by CitadelX",
                Shell = "rm -f /usr/local/bin/dnstt-server /usr/local/bin/dnstt-client"
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
            FileName = "dnstt.conf",
            Content = content,
            Format = NativeFormat.Text
        };
    }

    public string? BuildUserTemplate(UserTemplateRequest request)
        => string.IsNullOrWhiteSpace(request.UserTemplateJson) ? "{}" : request.UserTemplateJson;

    public SubscriptionPayload BuildSubscription(SubscriptionRequest request)
    {
        var values = ParseKeyValues(request.Config);
        var domain = Get(values, "domain", "t.example.com");
        var localListen = Get(values, "clientLocalListen", "127.0.0.1:1080");
        var forwardMode = NormalizeForwardMode(Get(values, "forwardMode", "socks5Sidecar"));
        var sidecarListen = Get(values, "sidecarListen", "127.0.0.1:10808");
        var sidecarInboundType = NormalizeSidecarInboundType(Get(values, "sidecarInboundType", "mixed"));
        var sidecarAuthEnabled = IsTrue(Get(values, "sidecarAuthEnabled", "false"));
        var sidecarUsername = Get(values, "sidecarUsername", "dnstt");
        var sidecarPassword = Get(values, "sidecarPassword", string.Empty);
        var targetAddress = Get(values, "targetAddress", "127.0.0.1:22");
        var effectiveTarget = forwardMode == "socks5Sidecar" ? sidecarListen : targetAddress;
        var publicKey = Get(values, "serverPublicKey", string.Empty);
        var clientMode = Get(values, "clientMode", "udp").ToLowerInvariant();
        var resolver = Get(values, "clientResolver", "8.8.8.8:53");
        var dohUrl = Get(values, "clientDohUrl", "https://dns.google/dns-query");
        var nameServerHost = Get(values, "nameServerHost", string.Empty);
        var nameServerAddress = Get(values, "nameServerAddress", string.Empty);

        var clientName = string.IsNullOrWhiteSpace(request.Label) ? request.UserId : request.Label;
        var args = BuildClientArgs(clientMode, resolver, dohUrl, publicKey, domain, localListen, clientName);
        var deeplink = BuildDnsttUri(domain, publicKey, clientMode, resolver, dohUrl, clientName);
        var file = $"""
        # CitadelX DnsTT client profile
        # DnsTT tunnels TCP through DNS. Start this local client, then point your application to {localListen}.
        #
        # Server forwards incoming tunnel TCP to: {effectiveTarget}
        {BuildSidecarClientNotes(forwardMode, sidecarInboundType, sidecarAuthEnabled, sidecarUsername, sidecarPassword, localListen)}
        # Required DNS delegation:
        #   {domain} NS {Fallback(nameServerHost, "<your-nameserver-host>")}
        #   {Fallback(nameServerHost, "<your-nameserver-host>")} A {Fallback(nameServerAddress, "<your-node-public-ip>")}
        #
        # Save the public key below as server.pub in the same directory as this profile:
        {NormalizePublicKeyBlock(publicKey)}

        # CitadelX deeplink:
        {(string.IsNullOrWhiteSpace(deeplink) ? "# <dnstt:// link will be available after serverPublicKey is filled>" : deeplink)}

        # Client command:
        dnstt-client {args}

        # Quick proxy test:
        #   curl --proxy socks5h://{localListen} https://api.ipify.org
        """;

        if (string.IsNullOrWhiteSpace(deeplink))
        {
            return SubscriptionPayload.ConfigFile(
                $"{Sanitize(request.UserId)}.dnstt.txt",
                Normalize(file),
                "text/plain");
        }

        return SubscriptionPayload.Combined(
            new[] { deeplink },
            $"{Sanitize(request.UserId)}.dnstt.txt",
            Normalize(file),
            "text/plain");
    }

    public ConfigArtifact ApplyNodeReport(ConfigArtifact artifact, JsonElement result)
    {
        var publicKey = ExtractPublicKey(result);
        if (string.IsNullOrWhiteSpace(publicKey) || artifact is not FileArtifact file)
        {
            return artifact;
        }

        return new FileArtifact
        {
            SchemaVersion = file.SchemaVersion,
            Placeholders = file.Placeholders,
            FileName = file.FileName,
            Content = UpsertKey(file.Content, "serverPublicKey", publicKey),
            Format = file.Format
        };
    }

    private static string BuildKeyValueConfig(JsonObject input)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# CitadelX-DnsTT");
        builder.AppendLine("# DNS setup example: t.example.com NS tns.example.com; tns.example.com A <node-public-ip>");
        Add(builder, "domain", GetString(input, "domain", "t.example.com"));
        Add(builder, "udpListen", GetString(input, "udpListen", ":5300"));
        Add(builder, "forwardMode", NormalizeForwardMode(GetString(input, "forwardMode", "socks5Sidecar")));
        Add(builder, "targetAddress", GetString(input, "targetAddress", "127.0.0.1:22"));
        Add(builder, "clientLocalListen", GetString(input, "clientLocalListen", "127.0.0.1:1080"));
        Add(builder, "sidecarListen", GetString(input, "sidecarListen", "127.0.0.1:10808"));
        Add(builder, "sidecarInboundType", NormalizeSidecarInboundType(GetString(input, "sidecarInboundType", "mixed")));
        Add(builder, "sidecarOutbound", NormalizeSidecarOutbound(GetString(input, "sidecarOutbound", "direct")));
        Add(builder, "sidecarAuthEnabled", GetBool(input, "sidecarAuthEnabled", false) ? "true" : "false");
        Add(builder, "sidecarUsername", GetString(input, "sidecarUsername", "dnstt"));
        Add(builder, "sidecarPassword", GetString(input, "sidecarPassword", string.Empty));
        Add(builder, "sidecarBinaryPath", GetString(input, "sidecarBinaryPath", string.Empty));
        Add(builder, "sidecarLogLevel", NormalizeLogLevel(GetString(input, "sidecarLogLevel", "info")));
        Add(builder, "clientMode", NormalizeClientMode(GetString(input, "clientMode", "udp")));
        Add(builder, "clientResolver", GetString(input, "clientResolver", "8.8.8.8:53"));
        Add(builder, "clientDohUrl", GetString(input, "clientDohUrl", "https://dns.google/dns-query"));
        Add(builder, "nameServerHost", GetString(input, "nameServerHost", "tns.example.com"));
        Add(builder, "nameServerAddress", GetString(input, "nameServerAddress", string.Empty));
        Add(builder, "serverPrivateKeyFile", GetString(input, "serverPrivateKeyFile", string.Empty));
        Add(builder, "serverPublicKeyFile", GetString(input, "serverPublicKeyFile", string.Empty));
        Add(builder, "serverPublicKey", GetString(input, "serverPublicKey", string.Empty));
        Add(builder, "notes", SingleLine(GetString(input, "notes", string.Empty)));
        return builder.ToString();
    }

    private static string BuildClientArgs(string mode, string resolver, string dohUrl, string publicKey, string domain, string localListen, string clientName)
    {
        var transport = mode switch
        {
            "doh" => $"-doh {Shell(dohUrl)}",
            "dot" => $"-dot {Shell(resolver)}",
            _ => $"-udp {Shell(resolver)}"
        };

        var keyArg = string.IsNullOrWhiteSpace(publicKey)
            ? "-pubkey-file server.pub"
            : "-pubkey-file server.pub";
        var nameArg = string.IsNullOrWhiteSpace(clientName) ? string.Empty : $"-n {Shell(clientName)}";
        return $"{transport} {keyArg} {nameArg} {Shell(domain)} {Shell(localListen)}".Replace("  ", " ", StringComparison.Ordinal).Trim();
    }

    private static string? BuildDnsttUri(string domain, string publicKey, string mode, string resolver, string dohUrl, string label)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(publicKey))
        {
            return null;
        }

        var transport = NormalizeClientMode(mode);
        var resolverValue = transport == "doh" ? dohUrl : resolver;
        var query = new[]
        {
            $"pubkey={Uri.EscapeDataString(publicKey.Trim())}",
            $"resolver={Uri.EscapeDataString(resolverValue.Trim())}",
            $"transport={Uri.EscapeDataString(transport)}"
        };
        var fragment = string.IsNullOrWhiteSpace(label) ? string.Empty : $"#{Uri.EscapeDataString(label.Trim())}";
        return $"dnstt://{Uri.EscapeDataString(domain.Trim())}?{string.Join('&', query)}{fragment}";
    }

    private static string BuildSidecarClientNotes(
        string forwardMode,
        string sidecarInboundType,
        bool sidecarAuthEnabled,
        string username,
        string password,
        string localListen)
    {
        if (forwardMode != "socks5Sidecar")
        {
            return "# Client mode: raw TCP forward. Use the local listener with the protocol served by the remote target.";
        }

        var protocol = sidecarInboundType == "mixed" ? "SOCKS5/HTTP proxy" : "SOCKS5 proxy";
        var auth = sidecarAuthEnabled
            ? $"# Local proxy auth: username={username}, password={password}"
            : "# Local proxy auth: disabled";
        return $"""
        # Client mode: {protocol}.
        # Configure your application to use SOCKS5 at {localListen}.
        {auth}
        """;
    }

    private static string NormalizePublicKeyBlock(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "# <server public key will be filled after the node applies the config>";
        }

        return value.Trim();
    }

    private static string? ExtractPublicKey(JsonElement result)
    {
        if (TryExtractPublicKey(result, out var publicKey))
        {
            return publicKey;
        }

        if (result.TryGetProperty("safeApply", out var safeApply)
            && safeApply.ValueKind == JsonValueKind.Object
            && safeApply.TryGetProperty("nodeReport", out var nodeReport)
            && TryExtractPublicKey(nodeReport, out publicKey))
        {
            return publicKey;
        }

        return null;
    }

    private static bool TryExtractPublicKey(JsonElement element, out string? publicKey)
    {
        publicKey = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty("dnstt", out var dnstt)
            && dnstt.ValueKind == JsonValueKind.Object
            && dnstt.TryGetProperty("publicKey", out var publicKeyProperty))
        {
            publicKey = publicKeyProperty.GetString();
            return !string.IsNullOrWhiteSpace(publicKey);
        }

        if (element.TryGetProperty("publicKey", out publicKeyProperty))
        {
            publicKey = publicKeyProperty.GetString();
            return !string.IsNullOrWhiteSpace(publicKey);
        }

        return false;
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

    private static string UpsertKey(string content, string key, string value)
    {
        var lines = Normalize(content).Split('\n').ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || !trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq >= 0 && string.Equals(trimmed[..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key} = {EscapeValue(value)}";
                return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
            }
        }

        lines.Add($"{key} = {EscapeValue(value)}");
        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
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

    private static string NormalizeClientMode(string value)
        => value.Equals("doh", StringComparison.OrdinalIgnoreCase)
            ? "doh"
            : value.Equals("dot", StringComparison.OrdinalIgnoreCase)
                ? "dot"
                : "udp";

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

    private static bool IsTrue(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("1", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

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
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return trimmed;
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
        return string.IsNullOrWhiteSpace(safe) ? "dnstt-client" : safe;
    }

    private static string Normalize(string content)
        => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + Environment.NewLine;
}
