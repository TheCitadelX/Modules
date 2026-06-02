using System.Text.Json;
using System.Text.Json.Nodes;
using CitadelX.Node.Abstractions;

namespace CitadelX.SingboxExtendedNodeModule;

public sealed class DisabledUserStore
{
    private readonly Func<string?> _configPathProvider;
    private readonly AtomicFileWriter _fileWriter;
    private readonly JsonSerializerOptions _serializerOptions = new() { WriteIndented = true };

    public DisabledUserStore(Func<string?> configPathProvider, AtomicFileWriter fileWriter)
    {
        _configPathProvider = configPathProvider;
        _fileWriter = fileWriter;
    }

    public void Save(string userId, JsonObject userObject)
    {
        var store = LoadInternal();
        store[userId] = userObject.ToJsonString();
        Persist(store);
    }

    public JsonObject? TryTake(string userId)
    {
        var store = LoadInternal();
        if (!store.TryGetValue(userId, out var payload))
        {
            return null;
        }

        store.Remove(userId);
        Persist(store);

        return JsonNode.Parse(payload) as JsonObject;
    }

    public void Remove(string userId)
    {
        var store = LoadInternal();
        if (!store.Remove(userId))
        {
            return;
        }

        Persist(store);
    }

    private Dictionary<string, string> LoadInternal()
    {
        var storePath = ResolveStorePath();
        if (!File.Exists(storePath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var content = File.ReadAllText(storePath);
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
        var store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (data is null)
        {
            return store;
        }

        foreach (var entry in data)
        {
            store[entry.Key] = entry.Value;
        }

        return store;
    }

    private void Persist(Dictionary<string, string> store)
    {
        var storePath = ResolveStorePath();
        var content = JsonSerializer.Serialize(store, _serializerOptions);
        _fileWriter.WriteAllTextAtomic(storePath, content);
    }

    private string ResolveStorePath()
    {
        var configPath = _configPathProvider();
        if (string.IsNullOrWhiteSpace(configPath))
        {
            configPath = Path.Combine(AppContext.BaseDirectory, "singbox.config.json");
        }

        var fullPath = Path.GetFullPath(configPath);
        var directory = Path.GetDirectoryName(fullPath) ?? AppContext.BaseDirectory;
        var fileName = Path.GetFileNameWithoutExtension(fullPath) + ".disabled-users.json";
        return Path.Combine(directory, fileName);
    }
}
