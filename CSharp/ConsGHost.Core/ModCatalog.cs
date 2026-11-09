using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace ConsGHost.Core;

public static partial class ModCatalog
{
    [GeneratedRegex("^[a-z][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static ScanResult Scan(string modsDirectory)
    {
        Directory.CreateDirectory(modsDirectory);
        List<ModSpec> mods = [];
        List<ModIssue> issues = [];
        foreach (var directory in System.IO.Directory.EnumerateDirectories(modsDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var metadataPath = Path.Combine(directory, "cghm_metadata.json");
            if (!File.Exists(metadataPath)) continue;
            try
            {
                SafePath(modsDirectory, Path.GetFileName(directory));
                var metadata = Json.Read<ModMetadata>(metadataPath);
                if (metadata.SchemaVersion != 1) throw new InvalidDataException("Unsupported schemaVersion; expected 1.");
                if (!IdPattern().IsMatch(metadata.Id)) throw new InvalidDataException("id must match [a-z][a-z0-9_.-]{0,63}.");
                if (string.IsNullOrWhiteSpace(metadata.Name)) throw new InvalidDataException("name is required.");
                ParseVersion(metadata.Version);
                if (metadata.Dependencies is null || metadata.LoadAfter is null) throw new InvalidDataException("Dependency lists cannot be null.");
                foreach (var dependency in metadata.Dependencies)
                {
                    if (dependency is null || !IdPattern().IsMatch(dependency.Id)) throw new InvalidDataException("Invalid dependency id.");
                    if (dependency.MinVersion != null) ParseVersion(dependency.MinVersion);
                    if (dependency.MaxVersionExclusive != null) ParseVersion(dependency.MaxVersionExclusive);
                }
                var managed = Assemblies(directory, "Managed", metadata.ManagedAssemblies);
                var native = Assemblies(directory, "Native", metadata.NativeLibraries);
                var addons = ScanAddons(directory, metadata.IgnoredAddons);
                foreach (var file in managed) _ = AssemblyName.GetAssemblyName(file);
                foreach (var file in native) ValidateX64(file, requireNativeDll: true);
                mods.Add(new(metadata, Path.GetFullPath(directory), managed, native) { Addons = addons });
            }
            catch (Exception ex) { issues.Add(new(directory, ex.Message)); }
        }
        return new(mods, issues);
    }

    public static List<ModSpec> Resolve(IEnumerable<ModSpec> source, IEnumerable<string> disabled)
    {
        var all = source.ToList();
        var duplicates = all.GroupBy(m => m.Metadata.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicates != null) throw new InvalidDataException($"Duplicate mod id: {duplicates.Key}");
        var excluded = disabled.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = all.Where(m => m.Metadata.Enabled && !excluded.Contains(m.Metadata.Id))
            .ToDictionary(m => m.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        List<ModSpec> ordered = [];
        Dictionary<string, int> states = new(StringComparer.OrdinalIgnoreCase);
        List<string> stack = [];
        void Visit(ModSpec mod)
        {
            var id = mod.Metadata.Id;
            if (states.TryGetValue(id, out var state))
            {
                if (state == 1) throw new InvalidDataException("Dependency cycle: " + string.Join(" -> ", stack.Append(id)));
                return;
            }
            states[id] = 1; stack.Add(id);
            foreach (var dependency in mod.Metadata.Dependencies)
            {
                if (!selected.TryGetValue(dependency.Id, out var target))
                {
                    if (!dependency.Optional) throw new InvalidDataException($"{id} requires missing/disabled mod {dependency.Id}.");
                    continue;
                }
                var version = ParseVersion(target.Metadata.Version);
                if (dependency.MinVersion != null && version < ParseVersion(dependency.MinVersion) ||
                    dependency.MaxVersionExclusive != null && version >= ParseVersion(dependency.MaxVersionExclusive))
                    throw new InvalidDataException($"{id}: incompatible version of {dependency.Id} ({target.Metadata.Version}).");
                Visit(target);
            }
            foreach (var after in mod.Metadata.LoadAfter) if (selected.TryGetValue(after, out var target)) Visit(target);
            stack.RemoveAt(stack.Count - 1); states[id] = 2; ordered.Add(mod);
        }
        foreach (var mod in selected.Values.OrderBy(m => m.Metadata.Id, StringComparer.Ordinal)) Visit(mod);
        return ordered;
    }

    public static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new InvalidDataException($"Expected major.minor.patch version: {value}");
        return version;
    }

    public static string SafePath(string root, string relative)
    {
        root = Path.GetFullPath(root);
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Absolute mod paths are forbidden.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Mod path escapes its directory.");
        for (var current = full; current.Length >= root.Length; current = Path.GetDirectoryName(current)!)
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Mod paths cannot traverse symbolic links or junctions.");
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }

    private static List<ModAddon> ScanAddons(string root, List<string> ignoredNames)
    {
        if (ignoredNames == null || ignoredNames.Any(name => string.IsNullOrWhiteSpace(name) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.TrimEnd(' ', '.') != name))
            throw new InvalidDataException("ignoredAddons must contain only addon folder or file names.");
        var ignored = ignoredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directory = SafePath(root, "Addons");
        List<ModAddon> addons = [];
        if (!Directory.Exists(directory)) return addons;
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool isFolder = (entry.Attributes & FileAttributes.Directory) != 0;
            if (!isFolder && !entry.Extension.Equals(".gma", StringComparison.OrdinalIgnoreCase)) continue;
            bool isIgnored = ignored.Contains(entry.Name);
            var path = isIgnored ? entry.FullName : SafePath(directory, entry.Name);
            addons.Add(new(entry.Name, path, !isFolder, isIgnored));
        }
        return addons;
    }

    private static List<string> Assemblies(string root, string folder, List<string>? declared)
    {
        var directory = SafePath(root, folder);
        var paths = declared?.Select(name => SafePath(directory, name)).ToList() ??
            (Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.dll").Order(StringComparer.OrdinalIgnoreCase).ToList() : []);
        foreach (var path in paths)
        {
            SafePath(root, Path.GetRelativePath(root, path));
            if (!File.Exists(path) || !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Missing DLL: {path}");
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void ValidateX64(string path, bool requireNativeDll = false)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64)
            throw new InvalidDataException($"Expected Windows x64 PE: {path}");
        if (requireNativeDll && (pe.PEHeaders.CorHeader != null || !pe.PEHeaders.CoffHeader.Characteristics.HasFlag(Characteristics.Dll)))
            throw new InvalidDataException($"Expected a native DLL, not a managed assembly or executable: {path}");
    }
}
