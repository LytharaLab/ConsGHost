using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConsGHost.SDK;

namespace ConsGHost.Core;

public sealed class ModConfig : IModConfig
{
    public const string DefaultFileName = "config.default.json";
    private readonly string _directory;
    private readonly string _lockName;
    public string FilePath { get; }

    private ModConfig(string directory)
    {
        _directory = Path.GetFullPath(directory);
        FilePath = Path.Combine(_directory, "config.json");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant()));
        _lockName = @"Local\CGH.Config." + Convert.ToHexString(hash);
    }

    public static ModConfig? Open(string directory)
    {
        if (!File.Exists(Path.Combine(directory, DefaultFileName))) return null;
        var config = new ModConfig(directory);
        config.Locked(() =>
        {
            string template = ModCatalog.SafePath(config._directory, DefaultFileName);
            string path = config.CheckPath();
            if (File.Exists(path))
                _ = config.ReadDefaults();
            else
                AtomicWrite(path, temp =>
                {
                    File.WriteAllBytes(temp, File.ReadAllBytes(template));
                    _ = ParseObject(File.ReadAllText(temp), DefaultFileName);
                }, overwrite: false);
            return true;
        });
        return config;
    }

    public string ReadRaw() => Locked(() => File.ReadAllText(CheckPath()));
    public JsonObject ReadDefaults() => ParseObject(
        File.ReadAllText(ModCatalog.SafePath(_directory, DefaultFileName)), DefaultFileName);

    public JsonObject ReadEffective(string raw)
    {
        var value = ParseObject(raw, "config.json");
        ApplyDefaults(value, ReadDefaults(), "");
        return value;
    }

    public JsonElement Read() => Locked(() => JsonSerializer.SerializeToElement(ReadEffective(File.ReadAllText(CheckPath()))));

    public void Set<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        Locked(() =>
        {
            var path = CheckPath();
            var current = ParseObject(File.ReadAllText(path), "config.json");
            current[key] = JsonSerializer.SerializeToNode(value);
            _ = ReadEffective(current.ToJsonString());
            AtomicWrite(path, temp => File.WriteAllText(temp, current.ToJsonString(Json.Options)), overwrite: true);
            return true;
        });
    }

    public void SaveRaw(string raw, string expected)
    {
        Locked(() =>
        {
            var path = CheckPath();
            if (File.ReadAllText(path) != expected)
                throw new IOException("config.json 已在其他地方修改，请重新打开编辑器再保存。");
            _ = ReadEffective(raw);
            AtomicWrite(path, temp => File.WriteAllText(temp, raw), overwrite: true);
            return true;
        });
    }

    private string CheckPath() => ModCatalog.SafePath(_directory, "config.json");

    private static JsonObject ParseObject(string text, string fileName)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Root must be a JSON object.");
            CheckUniqueKeys(document.RootElement);
            return JsonNode.Parse(text)!.AsObject();
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"{fileName}: {error.Message}", error);
        }
    }

    private static void CheckUniqueKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var item in element.EnumerateObject())
            {
                if (!names.Add(item.Name)) throw new JsonException($"Duplicate key '{item.Name}'.");
                CheckUniqueKeys(item.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CheckUniqueKeys(item);
        }
    }

    private static void ApplyDefaults(JsonObject current, JsonObject defaults, string prefix)
    {
        foreach (var (key, fallback) in defaults)
        {
            if (!current.TryGetPropertyValue(key, out var value))
            {
                current[key] = fallback?.DeepClone();
                continue;
            }
            if (fallback == null) continue;
            string path = prefix + key;
            if (fallback is JsonObject group && value is JsonObject currentGroup)
                ApplyDefaults(currentGroup, group, path + ".");
            else if (Kind(value) != Kind(fallback))
                throw new InvalidDataException($"config.json: '{path}' must be {Kind(fallback)}.");
        }
    }

    private static JsonValueKind Kind(JsonNode? node)
    {
        var kind = node?.GetValueKind() ?? JsonValueKind.Null;
        return kind == JsonValueKind.False ? JsonValueKind.True : kind;
    }

    private T Locked<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, _lockName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new IOException("Timed out waiting to access config.json.");
        }
        catch (AbandonedMutexException) { }
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void AtomicWrite(string path, Action<string> write, bool overwrite)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            write(temp);
            File.Move(temp, path, overwrite);
        }
        finally { File.Delete(temp); }
    }
}
