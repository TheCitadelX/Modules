using CitadelX.Backend.Cores;

namespace CitadelX.DnsTTModule;

internal static class DnsTTSimpleSetupSchema
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
                    { "key": "domain", "label": "Tunnel domain", "type": "text", "required": true, "placeholder": "t.example.com" },
                    { "key": "udpListen", "label": "Server UDP listen", "type": "text", "required": true, "placeholder": ":5300" },
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
                    { "key": "sidecarListen", "label": "Sidecar listen", "type": "text", "required": true, "placeholder": "127.0.0.1:10808" },
                    { "key": "sidecarInboundType", "label": "Inbound type", "type": "select", "options": [
                      { "value": "mixed", "label": "mixed (SOCKS + HTTP)" },
                      { "value": "socks", "label": "SOCKS only" }
                    ] },
                    { "key": "sidecarOutbound", "label": "Outbound", "type": "select", "options": [
                      { "value": "direct", "label": "direct" },
                      { "value": "block", "label": "block" }
                    ] },
                    { "key": "sidecarAuthEnabled", "label": "Require local auth", "type": "checkbox", "valueType": "boolean" },
                    { "key": "sidecarUsername", "label": "SOCKS username", "type": "text", "placeholder": "dnstt", "visibleWhen": { "field": "sidecarAuthEnabled", "equals": true }, "requiredWhen": { "field": "sidecarAuthEnabled", "equals": true } },
                    { "key": "sidecarPassword", "label": "SOCKS password", "type": "password", "placeholder": "password", "visibleWhen": { "field": "sidecarAuthEnabled", "equals": true }, "requiredWhen": { "field": "sidecarAuthEnabled", "equals": true } }
                  ]
                },
                {
                  "title": "Client resolver",
                  "columns": 2,
                  "fields": [
                    { "key": "clientMode", "label": "Client mode", "type": "select", "options": [
                      { "value": "udp", "label": "UDP DNS" },
                      { "value": "doh", "label": "DNS over HTTPS" },
                      { "value": "dot", "label": "DNS over TLS" }
                    ] },
                    { "key": "clientResolver", "label": "UDP/DoT resolver", "type": "text", "placeholder": "8.8.8.8:53", "visibleWhen": { "anyOf": [ { "field": "clientMode", "equals": "udp" }, { "field": "clientMode", "equals": "dot" } ] } },
                    { "key": "clientDohUrl", "label": "DoH resolver URL", "type": "text", "placeholder": "https://dns.google/dns-query", "visibleWhen": { "field": "clientMode", "equals": "doh" } }
                  ]
                },
                {
                  "title": "DNS delegation",
                  "columns": 1,
                  "fields": [
                    { "key": "nameServerHost", "label": "Nameserver host", "type": "text", "placeholder": "tns.example.com" },
                    { "key": "nameServerAddress", "label": "Nameserver address", "type": "text", "placeholder": "203.0.113.10" }
                  ]
                },
                {
                  "title": "Advanced",
                  "columns": 2,
                  "visibleWhen": { "field": "advancedMode", "truthy": true },
                  "fields": [
                    { "key": "serverPrivateKeyFile", "label": "Private key file", "type": "text", "placeholder": "auto" },
                    { "key": "serverPublicKeyFile", "label": "Public key file", "type": "text", "placeholder": "auto" },
                    { "key": "sidecarBinaryPath", "label": "sing-box binary path", "type": "text", "placeholder": "auto / sing-box" },
                    { "key": "sidecarLogLevel", "label": "Sidecar log level", "type": "select", "options": [
                      { "value": "info", "label": "info" },
                      { "value": "debug", "label": "debug" },
                      { "value": "warn", "label": "warn" },
                      { "value": "error", "label": "error" }
                    ] },
                    { "key": "serverPublicKey", "label": "Server public key", "type": "textarea", "rows": 3, "placeholder": "auto-filled after apply" },
                    { "key": "notes", "label": "Notes", "type": "textarea", "rows": 3, "placeholder": "Optional operator notes" }
                  ]
                }
              ]
            }
            """,
            DefaultsJson = """
            {
              "domain": "t.example.com",
              "udpListen": ":5300",
              "forwardMode": "socks5Sidecar",
              "targetAddress": "127.0.0.1:22",
              "clientLocalListen": "127.0.0.1:1080",
              "sidecarListen": "127.0.0.1:10808",
              "sidecarInboundType": "mixed",
              "sidecarOutbound": "direct",
              "sidecarAuthEnabled": false,
              "sidecarUsername": "dnstt",
              "sidecarPassword": "",
              "sidecarBinaryPath": "",
              "sidecarLogLevel": "info",
              "clientMode": "udp",
              "clientResolver": "8.8.8.8:53",
              "clientDohUrl": "https://dns.google/dns-query",
              "nameServerHost": "tns.example.com",
              "nameServerAddress": "",
              "serverPrivateKeyFile": "",
              "serverPublicKeyFile": "",
              "serverPublicKey": "",
              "notes": "",
              "advancedMode": false
            }
            """
        };
}
