using CitadelX.Backend.Cores;

namespace CitadelX.SlipstreamModule;

internal static class SlipstreamSimpleSetupSchema
{
    public static CoreConfigSchema Create()
        => new()
        {
            SchemaJson = """
            {
              "sections": [
                {
                  "title": "DNS tunnel",
                  "columns": 2,
                  "fields": [
                    { "key": "domain", "label": "Tunnel domain", "type": "text", "required": true, "placeholder": "slip.example.com" },
                    { "key": "udpListen", "label": "Server UDP listen", "type": "text", "required": true, "placeholder": ":53" },
                    { "key": "forwardMode", "label": "Forward mode", "type": "select", "options": [
                      { "value": "socks5Sidecar", "label": "Local SOCKS5 sidecar" },
                      { "value": "rawTcp", "label": "Raw TCP target" }
                    ] },
                    { "key": "targetAddress", "label": "Forward TCP target", "type": "text", "requiredWhen": { "field": "forwardMode", "equals": "rawTcp" }, "placeholder": "127.0.0.1:22", "visibleWhen": { "field": "forwardMode", "equals": "rawTcp" } },
                    { "key": "clientLocalListen", "label": "Client local listen", "type": "text", "required": true, "placeholder": "127.0.0.1:1080" },
                    { "key": "advancedMode", "label": "Advanced", "type": "checkbox", "valueType": "boolean" }
                  ]
                },
                {
                  "title": "SOCKS5 sidecar",
                  "columns": 2,
                  "visibleWhen": { "field": "forwardMode", "equals": "socks5Sidecar" },
                  "fields": [
                    { "key": "sidecarListen", "label": "Sidecar listen", "type": "text", "required": true, "placeholder": "127.0.0.1:10818" },
                    { "key": "sidecarInboundType", "label": "Inbound type", "type": "select", "options": [
                      { "value": "mixed", "label": "mixed (SOCKS + HTTP)" },
                      { "value": "socks", "label": "SOCKS only" }
                    ] },
                    { "key": "sidecarOutbound", "label": "Outbound", "type": "select", "options": [
                      { "value": "direct", "label": "direct" },
                      { "value": "block", "label": "block" }
                    ] },
                    { "key": "sidecarAuthEnabled", "label": "Require local auth", "type": "checkbox", "valueType": "boolean" },
                    { "key": "sidecarUsername", "label": "SOCKS username", "type": "text", "placeholder": "slipstream", "visibleWhen": { "field": "sidecarAuthEnabled", "equals": true }, "requiredWhen": { "field": "sidecarAuthEnabled", "equals": true } },
                    { "key": "sidecarPassword", "label": "SOCKS password", "type": "password", "placeholder": "password", "visibleWhen": { "field": "sidecarAuthEnabled", "equals": true }, "requiredWhen": { "field": "sidecarAuthEnabled", "equals": true } }
                  ]
                },
                {
                  "title": "Mobile client",
                  "columns": 2,
                  "fields": [
                    { "key": "clientResolvers", "label": "Recursive resolvers", "type": "textarea", "rows": 3, "placeholder": "1.1.1.1:53\\n8.8.8.8:53" },
                    { "key": "clientAuthoritativeResolvers", "label": "Authoritative paths", "type": "textarea", "rows": 3, "placeholder": "203.0.113.10:53" },
                    { "key": "clientCongestionControl", "label": "Congestion control", "type": "select", "options": [
                      { "value": "", "label": "auto" },
                      { "value": "bbr", "label": "bbr" },
                      { "value": "dcubic", "label": "dcubic" }
                    ] },
                    { "key": "clientKeepAliveMs", "label": "Keep alive (ms)", "type": "number", "min": 50, "max": 5000 }
                  ]
                },
                {
                  "title": "DNS delegation",
                  "columns": 1,
                  "actions": [
                    {
                      "kind": "validateDnsDelegation",
                      "label": "Validate delegation",
                      "help": "Check NS delegation, nameserver A/AAAA records, and UDP port readiness."
                    }
                  ],
                  "fields": [
                    { "key": "nameServerHost", "label": "Nameserver host", "type": "text", "placeholder": "ns-slip.example.com" },
                    { "key": "nameServerAddress", "label": "Nameserver address", "type": "text", "placeholder": "203.0.113.10" }
                  ]
                },
                {
                  "title": "Advanced",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "certPath", "label": "Server cert path", "type": "text", "placeholder": "auto cert.pem" },
                    { "key": "keyPath", "label": "Server key path", "type": "text", "placeholder": "auto key.pem" },
                    { "key": "resetSeedPath", "label": "Reset seed path", "type": "text", "placeholder": "auto reset-seed" },
                    { "key": "maxConnections", "label": "Max connections", "type": "number", "min": 1, "max": 10000 },
                    { "key": "idleTimeoutSeconds", "label": "Idle timeout seconds", "type": "number", "min": 0, "max": 86400 },
                    { "key": "fallbackUdp", "label": "UDP fallback target", "type": "text", "placeholder": "optional HOST:PORT" },
                    { "key": "sidecarBinaryPath", "label": "sing-box binary path", "type": "text", "placeholder": "auto / sing-box" },
                    { "key": "sidecarLogLevel", "label": "Sidecar log level", "type": "select", "options": [
                      { "value": "info", "label": "info" },
                      { "value": "debug", "label": "debug" },
                      { "value": "warn", "label": "warn" },
                      { "value": "error", "label": "error" }
                    ] },
                    { "key": "notes", "label": "Notes", "type": "textarea", "rows": 3, "placeholder": "Optional operator notes" }
                  ]
                }
              ]
            }
            """,
            DefaultsJson = """
            {
              "domain": "slip.example.com",
              "udpListen": ":53",
              "forwardMode": "socks5Sidecar",
              "targetAddress": "127.0.0.1:22",
              "clientLocalListen": "127.0.0.1:1080",
              "sidecarListen": "127.0.0.1:10818",
              "sidecarInboundType": "mixed",
              "sidecarOutbound": "direct",
              "sidecarAuthEnabled": false,
              "sidecarUsername": "slipstream",
              "sidecarPassword": "",
              "clientResolvers": "1.1.1.1:53\n8.8.8.8:53",
              "clientAuthoritativeResolvers": "",
              "clientCongestionControl": "",
              "clientKeepAliveMs": 400,
              "nameServerHost": "ns-slip.example.com",
              "nameServerAddress": "",
              "certPath": "",
              "keyPath": "",
              "resetSeedPath": "",
              "maxConnections": 256,
              "idleTimeoutSeconds": 60,
              "fallbackUdp": "",
              "sidecarBinaryPath": "",
              "sidecarLogLevel": "info",
              "notes": "",
              "advancedMode": false
            }
            """
        };
}
