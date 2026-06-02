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
                    ] }
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
                  "title": "Routing",
                  "columns": 2,
                  "fields": [
                    { "key": "dnsUpstreams", "label": "Client DNS upstreams", "type": "text", "placeholder": "1.1.1.1, tls://8.8.8.8" },
                    { "key": "ipv6Available", "label": "IPv6 available", "type": "checkbox", "valueType": "boolean" },
                    { "key": "allowPrivateNetworkConnections", "label": "Allow private networks", "type": "checkbox", "valueType": "boolean" }
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
              "dnsUpstreams": "1.1.1.1, tls://8.8.8.8",
              "ipv6Available": true,
              "allowPrivateNetworkConnections": false,
              "skipVerification": false
            }
            """
        };
}
