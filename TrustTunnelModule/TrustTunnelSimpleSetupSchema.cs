using CitadelX.Backend.Cores;

namespace CitadelX.TrustTunnelModule;

internal static class TrustTunnelSimpleSetupSchema
{
    public static CoreConfigSchema Create()
        => new()
        {
            SchemaJson = """
            {
              "sections": [
                {
                  "title": "Endpoint",
                  "columns": 2,
                  "fields": [
                    { "key": "listenAddress", "label": "Listen address", "type": "text", "required": true, "placeholder": "0.0.0.0:443" },
                    { "key": "hostname", "label": "TLS hostname", "type": "text", "required": true, "placeholder": "vpn.example.com" },
                    { "key": "publicAddress", "label": "Public address", "type": "text", "placeholder": "vpn.example.com:443" },
                    { "key": "logLevel", "label": "Log level", "type": "select", "options": [
                      { "value": "info", "label": "info" },
                      { "value": "debug", "label": "debug" },
                      { "value": "trace", "label": "trace" }
                    ] },
                    { "key": "advancedMode", "label": "Advanced", "type": "checkbox", "valueType": "boolean" }
                  ]
                },
                {
                  "title": "TLS",
                  "columns": 2,
                  "fields": [
                    { "key": "certChainPath", "label": "Certificate chain", "type": "text", "required": true, "placeholder": "certs/cert.pem" },
                    { "key": "privateKeyPath", "label": "Private key", "type": "text", "required": true, "placeholder": "certs/key.pem" },
                    { "key": "skipVerification", "label": "Skip client TLS verification", "type": "checkbox", "valueType": "boolean" }
                  ]
                },
                {
                  "title": "Service paths",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "pingEnable", "label": "Ping endpoint", "type": "checkbox", "valueType": "boolean" },
                    { "key": "pingPath", "label": "Ping path", "type": "text", "placeholder": "/ping", "disabledWhen": { "field": "pingEnable", "equals": false } },
                    { "key": "speedtestEnable", "label": "Speedtest endpoint", "type": "checkbox", "valueType": "boolean" },
                    { "key": "speedtestPath", "label": "Speedtest path", "type": "text", "placeholder": "/speedtest", "disabledWhen": { "field": "speedtestEnable", "equals": false } },
                    { "key": "authFailureStatusCode", "label": "Auth failure status", "type": "select", "options": [
                      { "value": "407", "label": "407 Proxy Authentication Required" },
                      { "value": "405", "label": "405 Method Not Allowed" },
                      { "value": "404", "label": "404 Not Found" },
                      { "value": "403", "label": "403 Forbidden" }
                    ] }
                  ]
                },
                {
                  "title": "Routing",
                  "columns": 2,
                  "fields": [
                    { "key": "dnsUpstreams", "label": "Client DNS upstreams", "type": "text", "placeholder": "1.1.1.1, 8.8.8.8" },
                    { "key": "ipv6Available", "label": "IPv6 available", "type": "checkbox", "valueType": "boolean" },
                    { "key": "allowPrivateNetworkConnections", "label": "Allow private networks", "type": "checkbox", "valueType": "boolean" }
                  ]
                },
                {
                  "title": "Forwarding",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "forwardProtocol", "label": "Forward protocol", "type": "select", "options": [
                      { "value": "direct", "label": "Direct" },
                      { "value": "socks5", "label": "SOCKS5 upstream" }
                    ] },
                    { "key": "socks5Address", "label": "SOCKS5 address", "type": "text", "placeholder": "127.0.0.1:1080", "visibleWhen": { "field": "forwardProtocol", "equals": "socks5" }, "requiredWhen": { "field": "forwardProtocol", "equals": "socks5" } },
                    { "key": "socks5ExtendedAuth", "label": "Extended SOCKS auth", "type": "checkbox", "valueType": "boolean", "visibleWhen": { "field": "forwardProtocol", "equals": "socks5" } }
                  ]
                },
                {
                  "title": "Reverse proxy",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "reverseProxyEnabled", "label": "Enable reverse proxy", "type": "checkbox", "valueType": "boolean" },
                    { "key": "reverseProxyServerAddress", "label": "Origin address", "type": "text", "placeholder": "127.0.0.1:8080", "disabledWhen": { "field": "reverseProxyEnabled", "equals": false } },
                    { "key": "reverseProxyPathMask", "label": "Path mask", "type": "text", "placeholder": "/api", "disabledWhen": { "field": "reverseProxyEnabled", "equals": false } },
                    { "key": "reverseProxyHostname", "label": "Reverse proxy hostname", "type": "text", "placeholder": "api.example.com", "disabledWhen": { "field": "reverseProxyEnabled", "equals": false } },
                    { "key": "reverseProxyH3BackwardCompatibility", "label": "H3 backward compatibility", "type": "checkbox", "valueType": "boolean", "disabledWhen": { "field": "reverseProxyEnabled", "equals": false } }
                  ]
                },
                {
                  "title": "ICMP and metrics",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "icmpEnabled", "label": "Enable ICMP forwarding", "type": "checkbox", "valueType": "boolean" },
                    { "key": "icmpInterfaceName", "label": "ICMP interface", "type": "text", "placeholder": "eth0", "disabledWhen": { "field": "icmpEnabled", "equals": false } },
                    { "key": "icmpRequestTimeoutSecs", "label": "ICMP timeout", "type": "number", "valueType": "number", "min": 1, "disabledWhen": { "field": "icmpEnabled", "equals": false } },
                    { "key": "icmpRecvQueueCapacity", "label": "ICMP queue capacity", "type": "number", "valueType": "number", "min": 1, "disabledWhen": { "field": "icmpEnabled", "equals": false } },
                    { "key": "metricsEnabled", "label": "Enable metrics", "type": "checkbox", "valueType": "boolean" },
                    { "key": "metricsAddress", "label": "Metrics address", "type": "text", "placeholder": "127.0.0.1:1987", "disabledWhen": { "field": "metricsEnabled", "equals": false } },
                    { "key": "metricsRequestTimeoutSecs", "label": "Metrics timeout", "type": "number", "valueType": "number", "min": 1, "disabledWhen": { "field": "metricsEnabled", "equals": false } }
                  ]
                },
                {
                  "title": "Rules",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "denyCidrs", "label": "Deny CIDRs", "type": "textarea", "rows": 3, "placeholder": "192.168.1.0/24, 10.0.0.0/8" },
                    { "key": "allowCidrs", "label": "Allow CIDRs", "type": "textarea", "rows": 3, "placeholder": "203.0.113.0/24" },
                    { "key": "rulesToml", "label": "Extra rules TOML", "type": "textarea", "rows": 5, "placeholder": "[[rule]]\\naction = \\\"deny\\\"" }
                  ]
                }
              ]
            }
            """,
            DefaultsJson = """
            {
              "listenAddress": "0.0.0.0:443",
              "hostname": "vpn.example.com",
              "certChainPath": "certs/cert.pem",
              "privateKeyPath": "certs/key.pem",
              "publicAddress": "",
              "logLevel": "info",
              "advancedMode": false,
              "dnsUpstreams": "1.1.1.1, 8.8.8.8",
              "ipv6Available": true,
              "allowPrivateNetworkConnections": false,
              "skipVerification": false,
              "pingEnable": false,
              "pingPath": "/ping",
              "speedtestEnable": false,
              "speedtestPath": "/speedtest",
              "authFailureStatusCode": "407",
              "forwardProtocol": "direct",
              "socks5Address": "127.0.0.1:1080",
              "socks5ExtendedAuth": false,
              "reverseProxyEnabled": false,
              "reverseProxyServerAddress": "127.0.0.1:8080",
              "reverseProxyPathMask": "/api",
              "reverseProxyHostname": "",
              "reverseProxyH3BackwardCompatibility": false,
              "icmpEnabled": false,
              "icmpInterfaceName": "eth0",
              "icmpRequestTimeoutSecs": 3,
              "icmpRecvQueueCapacity": 256,
              "metricsEnabled": false,
              "metricsAddress": "127.0.0.1:1987",
              "metricsRequestTimeoutSecs": 3,
              "denyCidrs": "",
              "allowCidrs": "",
              "rulesToml": ""
            }
            """
        };
}
