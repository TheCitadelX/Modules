using CitadelX.Backend.Cores;

namespace CitadelX.AmneziaWGModule;

public static class AmneziaWGSimpleSetupSchema
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
          "title": "Amnezia obfuscation",
          "description": "Randomize these fields unless you need exact client-compatible values.",
          "columns": 4,
          "actions": [
            {
              "kind": "randomizeAmneziaWg",
              "label": "Randomize",
              "help": "Generate a fresh AmneziaWG obfuscation profile for this server."
            }
          ],
          "fields": [
            { "key": "jc", "label": "Jc", "type": "number", "valueType": "number", "min": 0 },
            { "key": "jmin", "label": "Jmin", "type": "number", "valueType": "number", "min": 0 },
            { "key": "jmax", "label": "Jmax", "type": "number", "valueType": "number", "min": 0 },
            { "key": "s1", "label": "S1", "type": "number", "valueType": "number", "min": 0 },
            { "key": "s2", "label": "S2", "type": "number", "valueType": "number", "min": 0 },
            { "key": "s3", "label": "S3", "type": "number", "valueType": "number", "min": 0 },
            { "key": "s4", "label": "S4", "type": "number", "valueType": "number", "min": 0 },
            { "key": "h1", "label": "H1", "type": "text", "placeholder": "1234 or 123-456" },
            { "key": "h2", "label": "H2", "type": "text", "placeholder": "1234 or 123-456" },
            { "key": "h3", "label": "H3", "type": "text", "placeholder": "1234 or 123-456" },
            { "key": "h4", "label": "H4", "type": "text", "placeholder": "1234 or 123-456" },
            { "key": "i1", "label": "I1", "type": "text", "placeholder": "<r 16>" },
            { "key": "i2", "label": "I2", "type": "text", "placeholder": "<b 0x1234>" },
            { "key": "i3", "label": "I3", "type": "text" },
            { "key": "i4", "label": "I4", "type": "text" },
            { "key": "i5", "label": "I5", "type": "text" }
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
      "interfaceName": "awg0",
      "interfaceAddress": "10.78.0.1/24",
      "listenPort": 51820,
      "serverPublicKey": "",
      "dns": "1.1.1.1",
      "mtu": 1420,
      "jc": 4,
      "jmin": 40,
      "jmax": 70,
      "s1": 80,
      "s2": 120,
      "s3": "",
      "s4": "",
      "h1": "1",
      "h2": "2",
      "h3": "3",
      "h4": "4",
      "i1": "",
      "i2": "",
      "i3": "",
      "i4": "",
      "i5": "",
      "table": "",
      "postUp": "sysctl -w net.ipv4.ip_forward=1; iptables -t nat -A POSTROUTING -s 10.78.0.0/24 -j MASQUERADE",
      "postDown": "iptables -t nat -D POSTROUTING -s 10.78.0.0/24 -j MASQUERADE"
    }
    """;
}
