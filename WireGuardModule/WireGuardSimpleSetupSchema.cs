using CitadelX.Backend.Cores;

namespace CitadelX.WireGuardModule;

public static class WireGuardSimpleSetupSchema
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
          "title": "Interface",
          "columns": 3,
          "fields": [
            { "key": "interfaceName", "label": "Interface", "type": "text", "required": true },
            { "key": "interfaceAddress", "label": "Address", "type": "text", "required": true },
            { "key": "listenPort", "label": "Listen port", "type": "number", "valueType": "number", "min": 1, "required": true },
            { "key": "serverPublicKey", "label": "Server public key", "type": "text", "placeholder": "Optional until generated" },
            { "key": "dns", "label": "Client DNS", "type": "text", "placeholder": "1.1.1.1, 8.8.8.8" },
            { "key": "mtu", "label": "MTU", "type": "number", "valueType": "number", "min": 576 }
          ]
        },
        {
          "title": "Advanced",
          "columns": 2,
          "fields": [
            { "key": "table", "label": "Routing table", "type": "text", "placeholder": "auto" },
            { "key": "postUp", "label": "PostUp", "type": "textarea", "rows": 2 },
            { "key": "postDown", "label": "PostDown", "type": "textarea", "rows": 2 }
          ]
        }
      ]
    }
    """;

    private const string DefaultsJson = """
    {
      "interfaceName": "wg0",
      "interfaceAddress": "10.77.0.1/24",
      "listenPort": 51820,
      "serverPublicKey": "",
      "dns": "1.1.1.1",
      "mtu": 1420,
      "table": "",
      "postUp": "sysctl -w net.ipv4.ip_forward=1; iptables -t nat -A POSTROUTING -s 10.77.0.0/24 -j MASQUERADE",
      "postDown": "iptables -t nat -D POSTROUTING -s 10.77.0.0/24 -j MASQUERADE"
    }
    """;
}
