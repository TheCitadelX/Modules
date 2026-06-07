using CitadelX.Backend.Cores;

namespace CitadelX.SingboxModule;

/// <summary>
/// Schema-driven guided setup for sing-box. The form is intentionally recipe-free:
/// protocol, security, and transport are independent choices so the generated config
/// always reflects the visible admin input.
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
          "title": "Server",
          "description": "Minimal runnable server config. Start here; open Advanced only when you need outbound, routing, or socket tuning.",
          "columns": 3,
          "fields": [
            { "key": "inboundType", "label": "Protocol", "type": "select", "options": [
              { "value": "vless", "label": "VLESS" },
              { "value": "trojan", "label": "Trojan" },
              { "value": "vmess", "label": "VMess" },
              { "value": "shadowsocks", "label": "Shadowsocks" },
              { "value": "mixed", "label": "Mixed HTTP/SOCKS" },
              { "value": "socks", "label": "SOCKS" },
              { "value": "http", "label": "HTTP" },
              { "value": "direct", "label": "Direct" }
            ] },
            { "key": "inboundListen", "label": "Listen", "type": "text", "required": true, "placeholder": "0.0.0.0" },
            { "key": "inboundPort", "label": "Port", "type": "number", "valueType": "number", "min": 1, "required": true },
            { "key": "inboundSecurity", "label": "Security", "type": "select", "visibleWhen": { "field": "inboundType", "in": ["vless", "trojan", "vmess"] }, "options": [
              { "value": "reality", "label": "Reality" },
              { "value": "tls", "label": "TLS certificate" },
              { "value": "none", "label": "None" }
            ] },
            { "key": "advancedMode", "label": "Advanced", "type": "checkbox" }
          ]
        },
        {
          "title": "Authentication",
          "columns": 2,
          "visibleWhen": { "field": "inboundType", "in": ["mixed", "socks", "http", "shadowsocks"] },
          "fields": [
            { "key": "inboundProxyUsername", "label": "Username", "type": "text", "visibleWhen": { "field": "inboundType", "in": ["mixed", "socks", "http"] } },
            { "key": "inboundPassword", "label": "Password", "type": "text", "visibleWhen": { "field": "inboundType", "in": ["mixed", "socks", "http", "shadowsocks"] } },
            { "key": "inboundMethod", "label": "Method", "type": "select", "visibleWhen": { "field": "inboundType", "equals": "shadowsocks" }, "options": [
              { "value": "2022-blake3-aes-128-gcm", "label": "2022-blake3-aes-128-gcm" },
              { "value": "2022-blake3-aes-256-gcm", "label": "2022-blake3-aes-256-gcm" },
              { "value": "2022-blake3-chacha20-poly1305", "label": "2022-blake3-chacha20-poly1305" },
              { "value": "aes-128-gcm", "label": "aes-128-gcm" },
              { "value": "aes-256-gcm", "label": "aes-256-gcm" },
              { "value": "chacha20-ietf-poly1305", "label": "chacha20-ietf-poly1305" }
            ] },
            { "key": "inboundSetSystemProxy", "label": "Set system proxy", "type": "checkbox", "visibleWhen": { "field": "inboundType", "in": ["mixed", "socks", "http"] } }
          ]
        },
        {
          "title": "TLS",
          "columns": 2,
          "visibleWhen": { "allOf": [ { "field": "inboundType", "in": ["vless", "trojan", "vmess"] }, { "field": "inboundSecurity", "in": ["tls", "reality"] } ] },
          "fields": [
            { "key": "inboundTlsServerName", "label": "SNI / server name", "type": "text", "placeholder": "www.cloudflare.com" },
            { "key": "inboundTlsAlpn", "label": "ALPN", "type": "text", "visibleWhen": { "field": "inboundSecurity", "equals": "tls" }, "placeholder": "h2,http/1.1" },
            { "key": "inboundTlsCertificatePath", "label": "Certificate path", "type": "text", "visibleWhen": { "field": "inboundSecurity", "equals": "tls" }, "requiredWhen": { "field": "inboundSecurity", "equals": "tls" }, "placeholder": "/opt/citadelx/node/data/certificates/.../fullchain.pem" },
            { "key": "inboundTlsKeyPath", "label": "Key path", "type": "text", "visibleWhen": { "field": "inboundSecurity", "equals": "tls" }, "requiredWhen": { "field": "inboundSecurity", "equals": "tls" }, "placeholder": "/opt/citadelx/node/data/certificates/.../privkey.pem" }
          ]
        },
        {
          "title": "Reality",
          "description": "Leave private key and short id empty to generate them automatically.",
          "columns": 2,
          "visibleWhen": { "allOf": [ { "field": "inboundType", "in": ["vless", "trojan", "vmess"] }, { "field": "inboundSecurity", "equals": "reality" } ] },
          "fields": [
            { "key": "inboundRealityHandshakeServer", "label": "Handshake host", "type": "text", "required": true, "placeholder": "www.cloudflare.com" },
            { "key": "inboundRealityHandshakePort", "label": "Handshake port", "type": "number", "valueType": "number", "min": 1, "required": true },
            { "key": "inboundRealityPrivateKey", "label": "Private key", "type": "text", "placeholder": "Auto-generated when empty" },
            { "key": "inboundRealityShortId", "label": "Short ID", "type": "text", "placeholder": "Auto-generated, 0-8 hex chars" },
            { "key": "inboundRealityMaxTimeDifference", "label": "Max time difference", "type": "text", "placeholder": "1m" }
          ]
        },
        {
          "title": "Transport",
          "columns": 2,
          "visibleWhen": { "allOf": [ { "field": "advancedMode", "truthy": true }, { "field": "inboundType", "in": ["vless", "trojan", "vmess"] } ] },
          "fields": [
            { "key": "inboundTransportType", "label": "Transport", "type": "select", "options": [
              { "value": "", "label": "None" },
              { "value": "ws", "label": "WebSocket" },
              { "value": "grpc", "label": "gRPC" },
              { "value": "http", "label": "HTTP" },
              { "value": "httpupgrade", "label": "HTTP Upgrade" },
              { "value": "quic", "label": "QUIC" }
            ] },
            { "key": "inboundTransportHost", "label": "Host", "type": "text", "visibleWhen": { "field": "inboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "inboundTransportPath", "label": "Path", "type": "text", "visibleWhen": { "field": "inboundTransportType", "in": ["ws", "http", "httpupgrade"] }, "placeholder": "/ws" },
            { "key": "inboundTransportMethod", "label": "HTTP method", "type": "text", "visibleWhen": { "field": "inboundTransportType", "equals": "http" }, "placeholder": "GET" },
            { "key": "inboundTransportGrpcServiceName", "label": "gRPC service", "type": "text", "visibleWhen": { "field": "inboundTransportType", "equals": "grpc" }, "placeholder": "TunService" }
          ]
        },
        {
          "title": "Inbound Advanced",
          "columns": 2,
          "visibleWhen": { "field": "advancedMode", "truthy": true },
          "fields": [
            { "key": "inboundNetwork", "label": "Network", "type": "select", "options": [
              { "value": "both", "label": "both" },
              { "value": "tcp", "label": "tcp" },
              { "value": "udp", "label": "udp" }
            ] },
            { "key": "inboundBindInterface", "label": "Bind interface", "type": "text" },
            { "key": "inboundRoutingMark", "label": "Routing mark", "type": "number", "valueType": "string", "min": 0, "validate": "nonNegativeInteger" },
            { "key": "inboundNetns", "label": "Net namespace", "type": "text" },
            { "key": "inboundTcpFastOpen", "label": "TCP fast open", "type": "checkbox" },
            { "key": "inboundTcpMultiPath", "label": "TCP multi path", "type": "checkbox" },
            { "key": "inboundReuseAddr", "label": "Reuse address", "type": "checkbox" },
            { "key": "inboundUdpFragment", "label": "UDP fragment", "type": "checkbox" },
            { "key": "inboundTcpKeepAlive", "label": "TCP keep alive", "type": "text", "placeholder": "15s" },
            { "key": "inboundUdpTimeout", "label": "UDP timeout", "type": "text", "placeholder": "5m" },
            { "key": "inboundDetour", "label": "Detour", "type": "text", "placeholder": "outbound tag" }
          ]
        },
        {
          "title": "Protocol Defaults",
          "columns": 2,
          "visibleWhen": { "allOf": [ { "field": "advancedMode", "truthy": true }, { "field": "inboundType", "in": ["vless", "trojan", "vmess", "direct"] } ] },
          "fields": [
            { "key": "inboundUserName", "label": "Initial user name", "type": "text", "visibleWhen": { "field": "inboundType", "in": ["vless", "trojan", "vmess"] }, "placeholder": "Usually added later from Users" },
            { "key": "inboundUserUuid", "label": "Initial UUID", "type": "text", "visibleWhen": { "field": "inboundType", "in": ["vless", "vmess"] }, "placeholder": "Optional; users can be attached later" },
            { "key": "inboundUserPassword", "label": "Initial password", "type": "text", "visibleWhen": { "field": "inboundType", "equals": "trojan" }, "placeholder": "Optional; users can be attached later" },
            { "key": "inboundUserFlow", "label": "Initial VLESS flow", "type": "select", "visibleWhen": { "field": "inboundType", "equals": "vless" }, "options": [
              { "value": "", "label": "None" },
              { "value": "xtls-rprx-vision", "label": "xtls-rprx-vision" }
            ] },
            { "key": "inboundAlterId", "label": "VMess alter ID", "type": "number", "valueType": "number", "min": 0, "visibleWhen": { "field": "inboundType", "equals": "vmess" } },
            { "key": "inboundOverrideAddress", "label": "Direct override address", "type": "text", "visibleWhen": { "field": "inboundType", "equals": "direct" } },
            { "key": "inboundOverridePort", "label": "Direct override port", "type": "number", "valueType": "string", "min": 0, "validate": "nonNegativeInteger", "visibleWhen": { "field": "inboundType", "equals": "direct" } }
          ]
        },
        {
          "title": "Outbound",
          "columns": 3,
          "visibleWhen": { "field": "advancedMode", "truthy": true },
          "fields": [
            { "key": "outboundType", "label": "Outbound", "type": "select", "options": [
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
            { "key": "outboundPort", "label": "Port", "type": "number", "valueType": "number", "min": 1, "disabledWhen": { "field": "outboundType", "in": ["direct", "block"] }, "requiredWhen": { "field": "outboundType", "in": ["socks", "http", "shadowsocks", "vmess", "trojan", "vless"] } },
            { "key": "outboundUsername", "label": "Username", "type": "text", "visibleWhen": { "field": "outboundType", "in": ["socks", "http"] } },
            { "key": "outboundPassword", "label": "Password", "type": "text", "visibleWhen": { "field": "outboundType", "in": ["socks", "http", "shadowsocks", "trojan"] } },
            { "key": "outboundMethod", "label": "Method", "type": "text", "visibleWhen": { "field": "outboundType", "equals": "shadowsocks" } },
            { "key": "outboundVlessUuid", "label": "VLESS UUID", "type": "text", "visibleWhen": { "field": "outboundType", "equals": "vless" }, "requiredWhen": { "field": "outboundType", "equals": "vless" } },
            { "key": "outboundVmessUuid", "label": "VMess UUID", "type": "text", "visibleWhen": { "field": "outboundType", "equals": "vmess" }, "requiredWhen": { "field": "outboundType", "equals": "vmess" } },
            { "key": "outboundVlessFlow", "label": "VLESS flow", "type": "select", "visibleWhen": { "field": "outboundType", "equals": "vless" }, "options": [
              { "value": "", "label": "None" },
              { "value": "xtls-rprx-vision", "label": "xtls-rprx-vision" }
            ] }
          ]
        },
        {
          "title": "Outbound TLS & Transport",
          "columns": 2,
          "visibleWhen": { "allOf": [ { "field": "advancedMode", "truthy": true }, { "field": "outboundType", "in": ["http", "vmess", "trojan", "vless"] } ] },
          "fields": [
            { "key": "outboundTlsEnabled", "label": "Enable TLS", "type": "checkbox" },
            { "key": "outboundTlsServerName", "label": "Server name", "type": "text", "disabledWhen": { "field": "outboundTlsEnabled", "equals": false } },
            { "key": "outboundTlsAlpn", "label": "ALPN", "type": "text", "disabledWhen": { "field": "outboundTlsEnabled", "equals": false } },
            { "key": "outboundTlsInsecure", "label": "Skip certificate verification", "type": "checkbox", "disabledWhen": { "field": "outboundTlsEnabled", "equals": false } },
            { "key": "outboundTransportType", "label": "Transport", "type": "select", "visibleWhen": { "field": "outboundType", "in": ["vmess", "trojan", "vless"] }, "options": [
              { "value": "", "label": "None" },
              { "value": "ws", "label": "WebSocket" },
              { "value": "grpc", "label": "gRPC" },
              { "value": "http", "label": "HTTP" },
              { "value": "httpupgrade", "label": "HTTP Upgrade" },
              { "value": "quic", "label": "QUIC" }
            ] },
            { "key": "outboundTransportHost", "label": "Transport host", "type": "text", "visibleWhen": { "field": "outboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "outboundTransportPath", "label": "Transport path", "type": "text", "visibleWhen": { "field": "outboundTransportType", "in": ["ws", "http", "httpupgrade"] } },
            { "key": "outboundTransportGrpcServiceName", "label": "gRPC service", "type": "text", "visibleWhen": { "field": "outboundTransportType", "equals": "grpc" } }
          ]
        },
        {
          "title": "Routing",
          "columns": 2,
          "visibleWhen": { "field": "advancedMode", "truthy": true },
          "fields": [
            { "key": "routeFinal", "label": "Final outbound", "type": "select", "options": [
              { "value": "main-out", "label": "Main outbound" },
              { "value": "direct", "label": "Direct" },
              { "value": "block", "label": "Block" }
            ] },
            { "key": "routeAutoDetectInterface", "label": "Auto detect interface", "type": "checkbox" },
            { "key": "routeDirectDomains", "label": "Direct domains", "type": "textarea", "rows": 3, "placeholder": "example.com, .internal" },
            { "key": "routeDirectIpCidrs", "label": "Direct IP CIDR", "type": "textarea", "rows": 3, "placeholder": "10.0.0.0/8" },
            { "key": "routeBlockDomains", "label": "Block domains", "type": "textarea", "rows": 3, "placeholder": "ads.example.com" },
            { "key": "routeBlockIpCidrs", "label": "Block IP CIDR", "type": "textarea", "rows": 3, "placeholder": "203.0.113.0/24" }
          ]
        }
      ]
    }
    """;

    private const string DefaultsJson = """
    {
      "advancedMode": false,
      "inboundType": "vless",
      "inboundListen": "0.0.0.0",
      "inboundPort": 443,
      "inboundSecurity": "reality",
      "inboundTlsEnabled": true,
      "inboundTlsServerName": "www.cloudflare.com",
      "inboundTlsCertificatePath": "",
      "inboundTlsKeyPath": "",
      "inboundTlsAlpn": "h2,http/1.1",
      "inboundRealityHandshakeServer": "www.cloudflare.com",
      "inboundRealityHandshakePort": 443,
      "inboundRealityPrivateKey": "",
      "inboundRealityShortId": "",
      "inboundRealityMaxTimeDifference": "1m",
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
      "inboundUserFlow": "xtls-rprx-vision",
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
      "outboundServer": "",
      "outboundPort": 443,
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
