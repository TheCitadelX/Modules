using CitadelX.Backend.Cores;

namespace CitadelX.SingboxExtendedModule;

internal static class SingboxExtendedSimpleSetupSchema
{
    public static CoreConfigSchema Schema { get; } = new()
    {
        SchemaJson = """
        {
          "sections": [
            {
              "title": "Inbound",
              "columns": 3,
              "fields": [
                { "key": "inboundType", "label": "Type", "type": "select", "options": [
                  { "value": "mixed", "label": "Mixed" },
                  { "value": "socks", "label": "SOCKS" },
                  { "value": "http", "label": "HTTP" }
                ] },
                { "key": "inboundListen", "label": "Listen", "type": "text", "required": true },
                { "key": "inboundPort", "label": "Port", "type": "number", "valueType": "number", "min": 1, "required": true }
              ]
            },
            {
              "title": "Authentication",
              "columns": 2,
              "fields": [
                { "key": "username", "label": "Username", "type": "text" },
                { "key": "password", "label": "Password", "type": "text" }
              ]
            },
            {
              "title": "Outbound",
              "columns": 1,
              "fields": [
                { "key": "outboundType", "label": "Type", "type": "select", "options": [
                  { "value": "direct", "label": "Direct" },
                  { "value": "block", "label": "Block" }
                ] }
              ]
            }
          ]
        }
        """,
        DefaultsJson = """
        {
          "inboundType": "mixed",
          "inboundListen": "0.0.0.0",
          "inboundPort": 1080,
          "username": "",
          "password": "",
          "outboundType": "direct"
        }
        """
    };
}
