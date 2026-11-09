namespace ConsGHost.Core;

public static class SessionPreparation
{
    public static List<ModSpec> Prepare(string root, HostSettings settings)
    {
        CghVersion.VerifyDistribution(root);
        if (settings.GameArguments == null || settings.DisabledMods == null || settings.ExtraLibraries == null ||
            string.IsNullOrWhiteSpace(settings.GameDirectory) || string.IsNullOrWhiteSpace(settings.DotnetRoot))
            throw new InvalidDataException("Settings paths and lists cannot be null/empty.");
        if (settings.StartupTimeoutSeconds is < 10 or > 600) throw new InvalidDataException("Startup timeout must be 10-600 seconds.");
        var game = Path.Combine(settings.GameDirectory, "gmod_win64.exe");
        if (!File.Exists(game)) throw new FileNotFoundException("Choose the Garry's Mod directory containing gmod_win64.exe.", game);
        ModCatalog.ValidateX64(game);
        if (!Directory.Exists(Path.Combine(settings.DotnetRoot, "host", "fxr"))) throw new DirectoryNotFoundException(".NET root has no host/fxr: " + settings.DotnetRoot);
        var bootstrapConfig = Path.Combine(root, "ConsGHost.Bootstrap.runtimeconfig.json");
        if (!File.Exists(bootstrapConfig)) throw new FileNotFoundException("Incomplete CGH distribution: " + Path.GetFileName(bootstrapConfig), bootstrapConfig);
        var scan = ModCatalog.Scan(Path.Combine(root, "Mods"));
        if (scan.Issues.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, scan.Issues.Select(i => i.Directory + ": " + i.Message)));
        var mods = ModCatalog.Resolve(scan.Mods, settings.DisabledMods);
        if (!AddonsDisabled(settings) && mods.Any(mod => mod.Addons.Any(addon => !addon.Ignored)))
        {
            var installation = Path.GetFullPath(settings.GameDirectory);
            var gameRoot = Path.Combine(installation, "garrysmod");
            if (!Directory.Exists(gameRoot))
                throw new DirectoryNotFoundException("CGH addons require the garrysmod directory beside gmod_win64.exe.");
            for (int i = 0; i < settings.GameArguments.Count; i++)
            {
                if (!string.Equals(settings.GameArguments[i], "-game", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 == settings.GameArguments.Count ||
                    !Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.GameArguments[i + 1], installation))
                        .Equals(gameRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("CGH addons require the default GMod game directory. Remove the custom -game argument.");
            }
        }
        // Validate loader collisions per address space, including dependencies; Windows native modules are not ALC-isolated.
        var extras = ResolveExtras(settings);
        ValidateNativeNames(mods.Where(m => !m.Metadata.LoadAsSubprocess).SelectMany(m => m.Native).Concat(extras));
        foreach (var isolated in mods.Where(m => m.Metadata.LoadAsSubprocess)) ValidateNativeNames(isolated.Native);
        return mods;
    }

    public static bool AddonsDisabled(HostSettings settings) =>
        settings.GameArguments.Contains("-noaddons", StringComparer.OrdinalIgnoreCase);

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
