namespace ConsGHost.Core;

public static class SessionPreparation
{
    public static List<ModSpec> Prepare(string root, HostSettings settings)
    {
        if (settings.GameArguments == null || settings.DisabledMods == null || settings.ExtraLibraries == null ||
            string.IsNullOrWhiteSpace(settings.GameDirectory) || string.IsNullOrWhiteSpace(settings.DotnetRoot))
            throw new InvalidDataException("Settings paths and lists cannot be null/empty.");
        if (settings.StartupTimeoutSeconds is < 10 or > 600) throw new InvalidDataException("Startup timeout must be 10-600 seconds.");
        var game = Path.Combine(settings.GameDirectory, "gmod_win64.exe");
        if (!File.Exists(game)) throw new FileNotFoundException("Choose the Garry's Mod directory containing gmod_win64.exe.", game);
        ModCatalog.ValidateX64(game);
        if (!Directory.Exists(Path.Combine(settings.DotnetRoot, "host", "fxr"))) throw new DirectoryNotFoundException(".NET root has no host/fxr: " + settings.DotnetRoot);
        foreach (var name in new[] { "cgh_mrt_win64.exe", "cgh_runtime_win64.dll", "ConsGHost.Bootstrap.dll", "ConsGHost.Bootstrap.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(root, name))) throw new FileNotFoundException("Incomplete CGH distribution: " + name);
        var scan = ModCatalog.Scan(Path.Combine(root, "Mods"));
        if (scan.Issues.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, scan.Issues.Select(i => i.Directory + ": " + i.Message)));
        var mods = ModCatalog.Resolve(scan.Mods, settings.DisabledMods);
        // Validate loader collisions per address space, including dependencies; Windows native modules are not ALC-isolated.
        var extras = ResolveExtras(settings);
        ValidateNativeNames(mods.Where(m => !m.Metadata.LoadAsSubprocess).SelectMany(m => m.Native).Concat(extras));
        foreach (var isolated in mods.Where(m => m.Metadata.LoadAsSubprocess)) ValidateNativeNames(isolated.Native);
        return mods;
    }
    public static List<string> ResolveExtras(HostSettings settings)
    {
        List<string> libraries = [];
        foreach (var entry in settings.ExtraLibraries.Where(e => e.Enabled))
        {
            var path = Path.GetFullPath(entry.Path);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Extra DLL directory does not exist: " + path);
            foreach (var file in Directory.EnumerateFiles(path, "*.dll", entry.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
            {
                ModCatalog.ValidateX64(file, requireNativeDll: true); libraries.Add(file);
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static void ValidateNativeNames(IEnumerable<string> paths)
    {
        var duplicate = paths.Distinct(StringComparer.OrdinalIgnoreCase).GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidDataException("Ambiguous native DLL filename in one process: " + duplicate.Key);
    }
}
