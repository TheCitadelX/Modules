using CitadelX.Backend.Cores;

namespace CitadelX.SingboxModule;

/// <summary>
/// Schema-driven "Simple setup" descriptor for sing-box (§12 Step 7). The backend module owns the
/// form definition; the Frontend renders it generically from <see cref="CoreConfigSchema.SchemaJson"/>
/// and seeds values from <see cref="CoreConfigSchema.DefaultsJson"/>. Field keys are identical to the
/// flat structured object consumed by <see cref="SingboxConfigBuilder"/>, so the wizard submits these
/// values unchanged as a <c>ConfigInput</c> in <c>structured</c> mode.
/// </summary>
public static class SingboxSimpleSetupSchema
{
    public static CoreConfigSchema Schema { get; } = new()
    {
        SchemaJson = SchemaJson,
        DefaultsJson = DefaultsJson
    };

    private const string SchemaJson = """
    {
      "sections": [
        {
          "title": "Inbound",
          "columns": 3,
          "fields": [
            { "key": "inboundType", "label": "Type", "type": "select", "options": [
              { "value": "direct", "label": "Direct" },
              { "value": "mixed", "label": "Mixed" },
              { "value": "socks", "label": "SOCKS" },
              { "value": "http", "label": "HTTP" },
              { "value": "shadowsocks", "label": "Shadowsocks" },
              { "value": "vmess", "label": "VMess" },
              { "value": "trojan", "label": "Trojan" },
              { "value": "vless", "label": "VLESS" }
            ] },
            { "key": "inboundListen", "label": "Listen", "type": "text", "required": true },
            { "key": "inboundPort", "label": "Port", "type": "number", "valueType": "number", "min": 1, "required": true }
          ]
        },
        {
          "title": "Inbound Advanced",
          "columns": 2,
          "fields": [
            { "key": "inboundNetwork", "label": "Network", "type": "select", "options": [
              { "value": "both", "label": "both" },
              { "value": "tcp", "label": "tcp" },
              { "value": "udp", "label": "udp" }
            ] },
            { "key": "inboundBindInterface", "label": "Bind interface", "type": "text" },
            { "key": "inboundRoutingMark", "label": "Routing mark", "type": "number", "valueType": "string", "min": 0, "validate": "nonNegativeInteger" },
            { "key": "inboundNetns", "label": "Net namespace", "type": "text" },
            { "key": "inboundTcpKeepAlive", "label": "TCP keep alive", "type": "text", "placeholder": "15s" },
            { "key": "inboundUdpTimeout", "label": "UDP timeout", "type": "text", "placeholder": "5m" },
            { "key": "inboundDetour", "label": "Detour", "type": "text", "placeholder": "outbound tag" },
            { "key": "inboundTcpFastOpen", "label": "TCP fast open", "type": "checkbox" },
            { "key": "inboundTcpMultiPath", "label": "TCP multi path", "type": "checkbox" },
            { "key": "inboundReuseAddr", "label": "Reuse address", "type": "checkbox" },
            { "key": "inboundDisableTcpKeepAlive", "label": "Disable TCP keep alive", "type": "checkbox" },
            { "key": "inboundUdpFragment", "label": "UDP fragment", "type": "checkbox" }
          ]
        },
        {
          "title": "Inbound · Direct",
          "columns": 2,
          "visibleWhen": { "field": "inboundType", "equals": "direct" },
          "fields": [
            { "key": "inboundOverrideAddress", "label": "Override address", "type": "text" },
            { "key": "inboundOverridePort", "label": "Override port", "type": "number", "valueType": "string", "min": 0, "validate": "nonNegativeInteger" }
          ]
        },
        {
          "title": "Inbound · Authentication",
          "columns": 3,
          "visibleWhen": { "field": "inboundType", "in": ["mixed", "socks", "http"] },
          "fields": [
            { "key": "inboundProxyUsername", "label": "Username", "type": "text" },
            { "key": "inboundPassword", "label": "Password", "type": "text" },
            { "key": "inboundSetSystemProxy", "label": "Set system proxy", "type": "checkbox" }
          ]
        },
        {
          "title": "Inbound · Shadowsocks",
          "columns": 2,
          "visibleWhen": { "field": "inboundType", "equals": "shadowsocks" },
          "fields": [
            { "key": "inboundMethod", "label": "Method", "type": "select", "options": [
              { "value": "2022-blake3-aes-128-gcm", "label": "2022-blake3-aes-128-gcm" },
              { "value": "2022-blake3-aes-256-gcm", "label": "2022-blake3-aes-256-gcm" },
              { "value": "2022-blake3-chacha20-poly1305", "label": "2022-blake3-chacha20-poly1305" },
              { "value": "aes-128-gcm", "label": "aes-128-gcm" },
              { "value": "aes-256-gcm", "label": "aes-256-gcm" },
              { "value": "chacha20-ietf-poly1305", "label": "chacha20-ietf-poly1305" },
              { "value": "none", "label": "none" }
            ] },
            { "key": "inboundPassword", "label": "Password", "type": "text" }
          ]
        },
        {
          "title": "Inbound · User",
          "columns": 2,
          "visibleWhen": { "field": "inboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "inboundUserName", "label": "User name", "type": "text" },
            { "key": "inboundUserPassword", "label": "Password", "type": "text", "visibleWhen": { "field": "inboundType", "equals": "trojan" } },
            { "key": "inboundUserUuid", "label": "UUID", "type": "text", "visibleWhen": { "field": "inboundType", "in": ["vmess", "vless"] } },
            { "key": "inboundUserFlow", "label": "Flow", "type": "select", "visibleWhen": { "field": "inboundType", "equals": "vless" }, "options": [
              { "value": "", "label": "None" },
              { "value": "xtls-rprx-vision", "label": "xtls-rprx-vision" }
            ] },
            { "key": "inboundAlterId", "label": "Alter ID", "type": "number", "valueType": "number", "min": 0, "visibleWhen": { "field": "inboundType", "equals": "vmess" } }
          ]
        },
        {
          "title": "Inbound TLS",
          "columns": 2,
          "visibleWhen": { "field": "inboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "inboundTlsEnabled", "label": "Enable TLS", "type": "checkbox" },
            { "key": "inboundTlsServerName", "label": "Server name", "type": "text", "disabledWhen": { "field": "inboundTlsEnabled", "equals": false } },
            { "key": "inboundTlsAlpn", "label": "ALPN (comma)", "type": "text", "disabledWhen": { "field": "inboundTlsEnabled", "equals": false } },
            { "key": "inboundTlsCertificatePath", "label": "Certificate path", "type": "text", "disabledWhen": { "field": "inboundTlsEnabled", "equals": false }, "requiredWhen": { "allOf": [ { "field": "inboundType", "in": ["vless", "trojan"] }, { "field": "inboundTlsEnabled", "truthy": true } ] } },
            { "key": "inboundTlsKeyPath", "label": "Key path", "type": "text", "disabledWhen": { "field": "inboundTlsEnabled", "equals": false }, "requiredWhen": { "allOf": [ { "field": "inboundType", "in": ["vless", "trojan"] }, { "field": "inboundTlsEnabled", "truthy": true } ] } }
          ]
        },
        {
          "title": "Inbound Transport",
          "columns": 2,
          "visibleWhen": { "field": "inboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "inboundTransportType", "label": "Type", "type": "select", "options": [
              { "value": "", "label": "None" },
              { "value": "ws", "label": "WebSocket" },
              { "value": "grpc", "label": "gRPC" },
              { "value": "http", "label": "HTTP" },
              { "value": "httpupgrade", "label": "HTTP Upgrade" },
              { "value": "quic", "label": "QUIC" }
            ] },
            { "key": "inboundTransportHost", "label": "Host (comma for HTTP)", "type": "text", "visibleWhen": { "field": "inboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "inboundTransportPath", "label": "Path", "type": "text", "visibleWhen": { "field": "inboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "inboundTransportMethod", "label": "Method", "type": "text", "visibleWhen": { "field": "inboundTransportType", "equals": "http" } },
            { "key": "inboundTransportGrpcServiceName", "label": "Service name", "type": "text", "visibleWhen": { "field": "inboundTransportType", "equals": "grpc" } }
          ]
        },
        {
          "title": "Outbound",
          "columns": 3,
          "fields": [
            { "key": "outboundType", "label": "Type", "type": "select", "options": [
              { "value": "direct", "label": "Direct" },
              { "value": "block", "label": "Block" },
              { "value": "socks", "label": "SOCKS" },
              { "value": "http", "label": "HTTP" },
              { "value": "shadowsocks", "label": "Shadowsocks" },
              { "value": "vmess", "label": "VMess" },
              { "value": "trojan", "label": "Trojan" },
              { "value": "vless", "label": "VLESS" }
            ] },
            { "key": "outboundServer", "label": "Server", "type": "text", "disabledWhen": { "field": "outboundType", "in": ["direct", "block"] }, "requiredWhen": { "field": "outboundType", "in": ["socks", "http", "shadowsocks", "vmess", "trojan", "vless"] } },
            { "key": "outboundPort", "label": "Port", "type": "number", "valueType": "number", "min": 1, "disabledWhen": { "field": "outboundType", "in": ["direct", "block"] }, "requiredWhen": { "field": "outboundType", "in": ["socks", "http", "shadowsocks", "vmess", "trojan", "vless"] } }
          ]
        },
        {
          "title": "Outbound · SOCKS",
          "columns": 3,
          "visibleWhen": { "field": "outboundType", "equals": "socks" },
          "fields": [
            { "key": "outboundVersion", "label": "Version", "type": "select", "options": [
              { "value": "4", "label": "4" },
              { "value": "5", "label": "5" }
            ] },
            { "key": "outboundUsername", "label": "Username", "type": "text" },
            { "key": "outboundPassword", "label": "Password", "type": "text" }
          ]
        },
        {
          "title": "Outbound · HTTP",
          "columns": 2,
          "visibleWhen": { "field": "outboundType", "equals": "http" },
          "fields": [
            { "key": "outboundUsername", "label": "Username", "type": "text" },
            { "key": "outboundPassword", "label": "Password", "type": "text" },
            { "key": "outboundPath", "label": "Path", "type": "text" },
            { "key": "outboundHeadersJson", "label": "Headers JSON", "type": "text", "placeholder": "{\"User-Agent\":\"...\"}", "validate": "jsonObject" }
          ]
        },
        {
          "title": "Outbound · Shadowsocks",
          "columns": 2,
          "visibleWhen": { "field": "outboundType", "equals": "shadowsocks" },
          "fields": [
            { "key": "outboundMethod", "label": "Method", "type": "select", "options": [
              { "value": "2022-blake3-aes-128-gcm", "label": "2022-blake3-aes-128-gcm" },
              { "value": "2022-blake3-aes-256-gcm", "label": "2022-blake3-aes-256-gcm" },
              { "value": "2022-blake3-chacha20-poly1305", "label": "2022-blake3-chacha20-poly1305" },
              { "value": "aes-128-gcm", "label": "aes-128-gcm" },
              { "value": "aes-256-gcm", "label": "aes-256-gcm" },
              { "value": "chacha20-ietf-poly1305", "label": "chacha20-ietf-poly1305" },
              { "value": "none", "label": "none" }
            ] },
            { "key": "outboundPassword", "label": "Password", "type": "text", "requiredWhen": { "field": "outboundType", "equals": "shadowsocks" } }
          ]
        },
        {
          "title": "Outbound · Protocol",
          "columns": 2,
          "visibleWhen": { "field": "outboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "outboundVlessUuid", "label": "UUID", "type": "text", "visibleWhen": { "field": "outboundType", "equals": "vless" }, "requiredWhen": { "field": "outboundType", "equals": "vless" } },
            { "key": "outboundVmessUuid", "label": "UUID", "type": "text", "visibleWhen": { "field": "outboundType", "equals": "vmess" }, "requiredWhen": { "field": "outboundType", "equals": "vmess" } },
            { "key": "outboundPassword", "label": "Password", "type": "text", "visibleWhen": { "field": "outboundType", "equals": "trojan" }, "requiredWhen": { "field": "outboundType", "equals": "trojan" } },
            { "key": "outboundVlessFlow", "label": "Flow", "type": "select", "visibleWhen": { "field": "outboundType", "equals": "vless" }, "options": [
              { "value": "", "label": "None" },
              { "value": "xtls-rprx-vision", "label": "xtls-rprx-vision" }
            ] },
            { "key": "outboundVmessSecurity", "label": "Security", "type": "select", "visibleWhen": { "field": "outboundType", "equals": "vmess" }, "options": [
              { "value": "auto", "label": "auto" },
              { "value": "zero", "label": "zero" },
              { "value": "none", "label": "none" },
              { "value": "aes-128-gcm", "label": "aes-128-gcm" },
              { "value": "chacha20-poly1305", "label": "chacha20-poly1305" }
            ] },
            { "key": "outboundVmessAlterId", "label": "Alter ID", "type": "number", "valueType": "number", "min": 0, "visibleWhen": { "field": "outboundType", "equals": "vmess" } },
            { "key": "outboundVmessGlobalPadding", "label": "Global padding", "type": "checkbox", "visibleWhen": { "field": "outboundType", "equals": "vmess" } },
            { "key": "outboundVmessAuthenticatedLength", "label": "Authenticated length", "type": "checkbox", "visibleWhen": { "field": "outboundType", "equals": "vmess" } }
          ]
        },
        {
          "title": "Outbound · Network",
          "columns": 2,
          "visibleWhen": { "field": "outboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "outboundNetwork", "label": "Network", "type": "select", "options": [
              { "value": "tcp", "label": "tcp" },
              { "value": "udp", "label": "udp" },
              { "value": "both", "label": "both" }
            ] },
            { "key": "outboundPacketEncoding", "label": "Packet encoding", "type": "select", "visibleWhen": { "field": "outboundType", "in": ["vmess", "vless"] }, "options": [
              { "value": "", "label": "Default" },
              { "value": "packetaddr", "label": "packetaddr" },
              { "value": "xudp", "label": "xudp" }
            ] }
          ]
        },
        {
          "title": "Outbound TLS",
          "columns": 2,
          "visibleWhen": { "field": "outboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "outboundTlsEnabled", "label": "Enable TLS", "type": "checkbox" },
            { "key": "outboundTlsServerName", "label": "Server name", "type": "text", "disabledWhen": { "field": "outboundTlsEnabled", "equals": false } },
            { "key": "outboundTlsAlpn", "label": "ALPN (comma)", "type": "text", "disabledWhen": { "field": "outboundTlsEnabled", "equals": false } },
            { "key": "outboundTlsInsecure", "label": "Skip certificate verification", "type": "checkbox", "disabledWhen": { "field": "outboundTlsEnabled", "equals": false } }
          ]
        },
        {
          "title": "Outbound Transport",
          "columns": 2,
          "visibleWhen": { "field": "outboundType", "in": ["vmess", "trojan", "vless"] },
          "fields": [
            { "key": "outboundTransportType", "label": "Type", "type": "select", "options": [
              { "value": "", "label": "None" },
              { "value": "ws", "label": "WebSocket" },
              { "value": "grpc", "label": "gRPC" },
              { "value": "http", "label": "HTTP" },
              { "value": "httpupgrade", "label": "HTTP Upgrade" },
              { "value": "quic", "label": "QUIC" }
            ] },
            { "key": "outboundTransportHost", "label": "Host (comma for HTTP)", "type": "text", "visibleWhen": { "field": "outboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "outboundTransportPath", "label": "Path", "type": "text", "visibleWhen": { "field": "outboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "outboundTransportMethod", "label": "Method", "type": "text", "visibleWhen": { "field": "outboundTransportType", "equals": "http" } },
            { "key": "outboundTransportGrpcServiceName", "label": "Service name", "type": "text", "visibleWhen": { "field": "outboundTransportType", "equals": "grpc" } }
          ]
        },
        {
          "title": "Outbound Advanced",
          "columns": 2,
          "fields": [
            { "key": "outboundDomainResolver", "label": "Domain resolver", "type": "text", "placeholder": "dns server tag" },
            { "key": "outboundDetour", "label": "Detour tag", "type": "text", "placeholder": "optional" },
            { "key": "outboundBindInterface", "label": "Bind interface", "type": "text", "placeholder": "eth0" },
            { "key": "outboundInet4BindAddress", "label": "IPv4 bind address", "type": "text", "placeholder": "0.0.0.0" },
            { "key": "outboundInet6BindAddress", "label": "IPv6 bind address", "type": "text", "placeholder": "::" },
            { "key": "outboundRoutingMark", "label": "Routing mark", "type": "number", "valueType": "string", "min": 0, "placeholder": "optional", "validate": "nonNegativeInteger" },
            { "key": "outboundNetns", "label": "Net namespace", "type": "text", "placeholder": "optional" },
            { "key": "outboundConnectTimeout", "label": "Connect timeout", "type": "text", "placeholder": "5s" },
            { "key": "outboundTcpFastOpen", "label": "TCP fast open", "type": "checkbox" },
            { "key": "outboundTcpMultiPath", "label": "TCP multi path", "type": "checkbox" },
            { "key": "outboundReuseAddr", "label": "Reuse address", "type": "checkbox" },
            { "key": "outboundBindAddressNoPort", "label": "Bind address no port", "type": "checkbox" },
            { "key": "outboundDisableTcpKeepAlive", "label": "Disable TCP keep alive", "type": "checkbox" },
            { "key": "outboundUdpFragment", "label": "UDP fragment", "type": "checkbox" }
          ]
        },
        {
          "title": "Routing",
          "columns": 2,
          "fields": [
            { "key": "routeFinal", "label": "Final outbound", "type": "select", "options": [
              { "value": "main-out", "label": "Main outbound" },
              { "value": "direct", "label": "Direct" },
              { "value": "block", "label": "Block" }
            ] },
            { "key": "routeDefaultDomainResolver", "label": "Default domain resolver", "type": "text", "placeholder": "dns server tag" },
            { "key": "routeDefaultNetworkStrategy", "label": "Network strategy", "type": "select", "options": [
              { "value": "", "label": "Default" },
              { "value": "prefer_ipv4", "label": "Prefer IPv4" },
              { "value": "prefer_ipv6", "label": "Prefer IPv6" },
              { "value": "ipv4_only", "label": "IPv4 only" },
              { "value": "ipv6_only", "label": "IPv6 only" }
            ] },
            { "key": "routeDefaultNetworkType", "label": "Network type", "type": "select", "options": [
              { "value": "", "label": "Default" },
              { "value": "tcp", "label": "TCP" },
              { "value": "udp", "label": "UDP" }
            ] },
            { "key": "routeDefaultInterface", "label": "Default interface", "type": "text" },
            { "key": "routeDefaultMark", "label": "Default mark", "type": "number", "valueType": "string", "min": 0, "validate": "nonNegativeInteger" },
            { "key": "routeAutoDetectInterface", "label": "Auto detect interface", "type": "checkbox" },
            { "key": "routeFindProcess", "label": "Find process", "type": "checkbox" },
            { "key": "routeSniffEnabled", "label": "Sniff route action", "type": "checkbox" },
            { "key": "routeResolveEnabled", "label": "Resolve route action", "type": "checkbox" },
            { "key": "routeSniffTimeout", "label": "Sniff timeout", "type": "text", "placeholder": "300ms", "disabledWhen": { "field": "routeSniffEnabled", "equals": false } },
            { "key": "routeResolveServer", "label": "Resolve server", "type": "text", "placeholder": "dns server tag", "disabledWhen": { "field": "routeResolveEnabled", "equals": false } },
            { "key": "routeResolveStrategy", "label": "Resolve strategy", "type": "select", "disabledWhen": { "field": "routeResolveEnabled", "equals": false }, "options": [
              { "value": "", "label": "Default" },
              { "value": "prefer_ipv4", "label": "Prefer IPv4" },
              { "value": "prefer_ipv6", "label": "Prefer IPv6" },
              { "value": "ipv4_only", "label": "IPv4 only" },
              { "value": "ipv6_only", "label": "IPv6 only" }
            ] }
          ]
        },
        {
          "title": "Routing Lists",
          "columns": 2,
          "fields": [
            { "key": "routeDirectDomains", "label": "Direct domains", "type": "textarea", "rows": 3, "placeholder": "example.com, .internal" },
            { "key": "routeDirectIpCidrs", "label": "Direct IP CIDR", "type": "textarea", "rows": 3, "placeholder": "10.0.0.0/8, 192.168.0.0/16" },
            { "key": "routeBlockDomains", "label": "Block domains", "type": "textarea", "rows": 3, "placeholder": "ads.example.com" },
            { "key": "routeBlockIpCidrs", "label": "Block IP CIDR", "type": "textarea", "rows": 3, "placeholder": "203.0.113.0/24" }
          ]
        }
      ]
    }
    """;

    private const string DefaultsJson = """
    {
      "inboundType": "mixed",
      "inboundListen": "0.0.0.0",
      "inboundPort": 1080,
      "inboundTlsEnabled": false,
      "inboundTlsServerName": "",
      "inboundTlsCertificatePath": "",
      "inboundTlsKeyPath": "",
      "inboundTlsAlpn": "",
      "inboundTransportType": "",
      "inboundTransportHost": "",
      "inboundTransportPath": "",
      "inboundTransportMethod": "",
      "inboundTransportGrpcServiceName": "",
      "inboundNetwork": "both",
      "inboundProxyUsername": "",
      "inboundPassword": "",
      "inboundSetSystemProxy": false,
      "inboundOverrideAddress": "",
      "inboundOverridePort": "",
      "inboundMethod": "2022-blake3-aes-128-gcm",
      "inboundUserName": "",
      "inboundUserPassword": "",
      "inboundUserUuid": "",
      "inboundUserFlow": "",
      "inboundAlterId": 0,
      "inboundBindInterface": "",
      "inboundRoutingMark": "",
      "inboundReuseAddr": false,
      "inboundNetns": "",
      "inboundTcpFastOpen": false,
      "inboundTcpMultiPath": false,
      "inboundDisableTcpKeepAlive": false,
      "inboundTcpKeepAlive": "",
      "inboundTcpKeepAliveInterval": "",
      "inboundUdpFragment": false,
      "inboundUdpTimeout": "",
      "inboundDetour": "",
      "outboundType": "direct",
      "outboundServer": "127.0.0.1",
      "outboundPort": 1080,
      "outboundVersion": "5",
      "outboundUsername": "",
      "outboundPassword": "",
      "outboundPath": "",
      "outboundHeadersJson": "",
      "outboundMethod": "2022-blake3-aes-128-gcm",
      "outboundVmessUuid": "",
      "outboundVmessSecurity": "auto",
      "outboundVmessAlterId": 0,
      "outboundVmessGlobalPadding": false,
      "outboundVmessAuthenticatedLength": true,
      "outboundVlessUuid": "",
      "outboundVlessFlow": "",
      "outboundNetwork": "both",
      "outboundPacketEncoding": "",
      "outboundTlsEnabled": false,
      "outboundTlsServerName": "",
      "outboundTlsInsecure": false,
      "outboundTlsAlpn": "",
      "outboundTransportType": "",
      "outboundTransportHost": "",
      "outboundTransportPath": "",
      "outboundTransportMethod": "",
      "outboundTransportGrpcServiceName": "",
      "outboundDomainResolver": "",
      "outboundDetour": "",
      "outboundBindInterface": "",
      "outboundInet4BindAddress": "",
      "outboundInet6BindAddress": "",
      "outboundBindAddressNoPort": false,
      "outboundRoutingMark": "",
      "outboundReuseAddr": false,
      "outboundNetns": "",
      "outboundTcpFastOpen": false,
      "outboundTcpMultiPath": false,
      "outboundDisableTcpKeepAlive": false,
      "outboundTcpKeepAlive": "",
      "outboundTcpKeepAliveInterval": "",
      "outboundUdpFragment": false,
      "outboundConnectTimeout": "",
      "routeFinal": "main-out",
      "routeAutoDetectInterface": true,
      "routeDefaultInterface": "",
      "routeDefaultMark": "",
      "routeFindProcess": false,
      "routeDefaultDomainResolver": "",
      "routeDefaultNetworkStrategy": "",
      "routeDefaultNetworkType": "",
      "routeSniffEnabled": false,
      "routeSniffTimeout": "",
      "routeResolveEnabled": false,
      "routeResolveServer": "",
      "routeResolveStrategy": "",
      "routeDirectDomains": "",
      "routeDirectIpCidrs": "",
      "routeBlockDomains": "",
      "routeBlockIpCidrs": ""
    }
    """;
}
