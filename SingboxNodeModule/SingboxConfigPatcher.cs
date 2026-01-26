using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Node.Abstractions;

namespace CitadelX.SingboxNodeModule;

public sealed class SingboxConfigPatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public JsonNode LoadJson(string jsonOrPath)
    {
        var content = File.Exists(jsonOrPath) ? File.ReadAllText(jsonOrPath) : jsonOrPath;
        var node = JsonNode.Parse(content);
        if (node is null)
        {
            throw new InvalidOperationException("Failed to parse sing-box config.");
        }

        return node;
    }

    public string Serialize(JsonNode root)
    {
        return root.ToJsonString(SerializerOptions);
    }

    public string Normalize(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is null)
        {
            throw new InvalidOperationException("Failed to parse sing-box config.");
        }

        return Serialize(node);
    }

    public void AddUser(JsonNode root, UserEntity user, string? inboundType, string? inboundTag, JsonObject? userTemplate)
    {
        var inbound = FindInbound(root, inboundType, inboundTag);
        var resolvedType = inbound["type"]?.GetValue<string>() ?? inboundType ?? string.Empty;
        var userKey = ResolveUserKey(resolvedType);
        var users = GetOrCreateUsers(inbound);

        RemoveUserFromArray(users, userKey, user.Id);

        var userObject = CreateUserObject(user, userKey, userTemplate);
        users.Add(userObject);
    }

    public void EditUser(JsonNode root, UserEntity user, string? inboundType, string? inboundTag, JsonObject? userTemplate)
    {
        var inbound = FindInbound(root, inboundType, inboundTag);
        var resolvedType = inbound["type"]?.GetValue<string>() ?? inboundType ?? string.Empty;
        var userKey = ResolveUserKey(resolvedType);
        var users = GetOrCreateUsers(inbound);

        RemoveUserFromArray(users, userKey, user.Id);

        var userObject = CreateUserObject(user, userKey, userTemplate);
        users.Add(userObject);
    }

    public JsonObject? RemoveUser(JsonNode root, string userId, string? inboundType, string? inboundTag)
    {
        var inbound = FindInbound(root, inboundType, inboundTag);
        var resolvedType = inbound["type"]?.GetValue<string>() ?? inboundType ?? string.Empty;
        var userKey = ResolveUserKey(resolvedType);
        var users = GetOrCreateUsers(inbound);
        for (var i = users.Count - 1; i >= 0; i--)
        {
            if (users[i] is not JsonObject obj)
            {
                continue;
            }

            var value = obj[userKey]?.GetValue<string>();
            if (string.Equals(value, userId, StringComparison.OrdinalIgnoreCase))
            {
                users.RemoveAt(i);
                return obj;
            }
        }

        return null;
    }

    public List<string> RemoveUsersNotIn(JsonNode root, IReadOnlyCollection<string> allowedUserIds, string? inboundType, string? inboundTag)
    {
        var inbound = FindInbound(root, inboundType, inboundTag);
        var resolvedType = inbound["type"]?.GetValue<string>() ?? inboundType ?? string.Empty;
        var userKey = ResolveUserKey(resolvedType);
        var users = GetOrCreateUsers(inbound);
        var removed = new List<string>();

        for (var i = users.Count - 1; i >= 0; i--)
        {
            if (users[i] is not JsonObject obj)
            {
                continue;
            }

            var value = obj[userKey]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!allowedUserIds.Contains(value))
            {
                removed.Add(value);
                users.RemoveAt(i);
            }
        }

        return removed;
    }

    private static JsonObject FindInbound(JsonNode root, string? inboundType, string? inboundTag)
    {
        if (root is not JsonObject rootObj)
        {
            throw new InvalidOperationException("Config root must be an object.");
        }

        if (rootObj["inbounds"] is not JsonArray inbounds)
        {
            throw new InvalidOperationException("Config does not contain inbounds.");
        }

        foreach (var inboundNode in inbounds)
        {
            if (inboundNode is not JsonObject inbound)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(inboundTag))
            {
                var tag = inbound["tag"]?.GetValue<string>();
                if (string.Equals(tag, inboundTag, StringComparison.OrdinalIgnoreCase))
                {
                    return inbound;
                }
            }
        }

        foreach (var inboundNode in inbounds)
        {
            if (inboundNode is not JsonObject inbound)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(inboundType))
            {
                var type = inbound["type"]?.GetValue<string>();
                if (string.Equals(type, inboundType, StringComparison.OrdinalIgnoreCase))
                {
                    return inbound;
                }
            }
        }

        foreach (var inboundNode in inbounds)
        {
            if (inboundNode is JsonObject inbound && inbound["users"] is JsonArray)
            {
                return inbound;
            }
        }

        throw new InvalidOperationException("No suitable inbound found.");
    }

    private static JsonArray GetOrCreateUsers(JsonObject inbound)
    {
        if (inbound["users"] is JsonArray users)
        {
            return users;
        }

        var created = new JsonArray();
        inbound["users"] = created;
        return created;
    }

    private static void RemoveUserFromArray(JsonArray users, string userKey, string userId)
    {
        for (var i = users.Count - 1; i >= 0; i--)
        {
            if (users[i] is not JsonObject obj)
            {
                continue;
            }

            var value = obj[userKey]?.GetValue<string>();
            if (string.Equals(value, userId, StringComparison.OrdinalIgnoreCase))
            {
                users.RemoveAt(i);
            }
        }
    }

    private static JsonObject CreateUserObject(UserEntity user, string userKey, JsonObject? template)
    {
        var userObject = template is null
            ? new JsonObject()
            : JsonNode.Parse(template.ToJsonString()) as JsonObject ?? new JsonObject();

        userObject[userKey] = user.Id;
        return userObject;
    }

    private static string ResolveUserKey(string inboundType)
    {
        var normalized = inboundType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "mixed" => "username",
            "socks" => "username",
            "http" => "username",
            _ => "name"
        };
    }
}
