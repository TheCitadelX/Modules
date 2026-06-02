using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CitadelX.SingboxExtendedModule;

internal static class SingboxExtendedConfigBuilder
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Build(JsonObject input)
    {
        var inboundType = GetString(input, "inboundType") ?? "mixed";
        if (inboundType is not ("mixed" or "socks" or "http"))
        {
            inboundType = "mixed";
        }

        var inbound = new JsonObject
        {
            ["type"] = inboundType,
            ["tag"] = "main-in",
            ["listen"] = GetString(input, "inboundListen") ?? "0.0.0.0",
            ["listen_port"] = GetNumber(input, "inboundPort") ?? 1080
        };

        var username = GetString(input, "username");
        var password = GetString(input, "password");
        if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
        {
            inbound["users"] = new JsonArray(new JsonObject
            {
                ["username"] = username ?? string.Empty,
                ["password"] = password ?? string.Empty
            });
        }

        var outboundType = GetString(input, "outboundType") == "block" ? "block" : "direct";
        var config = new JsonObject
        {
            ["inbounds"] = new JsonArray(inbound),
            ["outbounds"] = new JsonArray(new JsonObject
            {
                ["type"] = outboundType,
                ["tag"] = "main-out"
            }),
            ["route"] = new JsonObject
            {
                ["final"] = "main-out",
                ["auto_detect_interface"] = true
            }
        };

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            config.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? GetString(JsonObject input, string key)
    {
        if (input[key] is null)
        {
            return null;
        }

        try
        {
            var value = input[key]!.GetValue<string>().Trim();
            return value.Length == 0 ? null : value;
        }
        catch
        {
            var value = input[key]!.ToString().Trim();
            return value.Length == 0 ? null : value;
        }
    }

    private static int? GetNumber(JsonObject input, string key)
    {
        if (input[key] is null)
        {
            return null;
        }

        try
        {
            return input[key]!.GetValue<int>();
        }
        catch
        {
            return int.TryParse(input[key]!.ToString(), out var value) ? value : null;
        }
    }
}
