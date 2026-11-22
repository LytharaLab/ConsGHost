using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConsGHost.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new InvalidDataException($"Empty JSON: {path}");
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Options)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed record ModDependency
{
    public required string Id { get; init; }
    public string? MinVersion { get; init; }
    public string? MaxVersionExclusive { get; init; }
    public bool Optional { get; init; }
}

public sealed record ModMetadata
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string Description { get; init; } = "";
    public string Author { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool LoadAsSubprocess { get; init; }
    public List<ModDependency> Dependencies { get; init; } = [];
    public List<string> LoadAfter { get; init; } = [];
    public List<string>? ManagedAssemblies { get; init; }
    public List<string>? NativeLibraries { get; init; }
    public List<string> IgnoredAddons { get; init; } = [];
}

public sealed record ModAddon(string Name, string Path, bool IsArchive, bool Ignored);
public sealed record ModSpec(ModMetadata Metadata, string Directory, List<string> Managed, List<string> Native)
{
    [JsonIgnore]
    public List<ModAddon> Addons { get; init; } = [];
    [JsonIgnore]
    public bool HasConfig { get; init; }
}
public sealed record ModIssue(string Directory, string Message);
public sealed record ScanResult(List<ModSpec> Mods, List<ModIssue> Issues);
public sealed record ExtraLibraryDirectory
{
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool Recursive { get; set; }
}
public sealed record HostSettings
{
    public string? UiLanguage { get; set; }
    public string GameDirectory { get; set; } = "";
    public List<string> GameArguments { get; set; } = ["-console", "-novid", "-insecure"];
    public List<string> DisabledMods { get; set; } = [];
    public List<ExtraLibraryDirectory> ExtraLibraries { get; set; } = [];
    public int StartupTimeoutSeconds { get; set; } = 90;
    public bool EnableEngineHooks { get; set; } = true;
    public string DotnetRoot { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
}

public sealed record RuntimePlan
{
    public required string RootDirectory { get; init; }
    public required string SessionDirectory { get; init; }
    public required string PipeName { get; init; }
    public required string Token { get; init; }
    public required string Endpoint { get; init; }
    public required List<ModSpec> Mods { get; init; }
    public List<string> ExtraLibraries { get; init; } = [];
    public bool IsIsolated { get; init; }
    public bool EnableEngineHooks { get; init; }
    public int StartupTimeoutSeconds { get; init; } = 90;
    public required string DotnetRoot { get; init; }
}
