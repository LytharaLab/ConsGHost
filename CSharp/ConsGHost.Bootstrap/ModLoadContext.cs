using System.Reflection;
using System.Runtime.Loader;
using ConsGHost.Core;
using ConsGHost.SDK;

namespace ConsGHost.Bootstrap;

internal sealed class ModLoadContext(ModSpec spec) : AssemblyLoadContext(spec.Metadata.Id, isCollectible: true)
{
    private readonly List<AssemblyDependencyResolver> _resolvers = spec.Managed.Select(p => new AssemblyDependencyResolver(p)).ToList();
    protected override Assembly? Load(AssemblyName name)
    {
        if (name.Name == typeof(ICghMod).Assembly.GetName().Name) return typeof(ICghMod).Assembly;
        foreach (var resolver in _resolvers)
        {
            var path = resolver.ResolveAssemblyToPath(name);
            if (path != null) return LoadFromAssemblyPath(Validate(path));
        }
        var local = ModCatalog.SafePath(spec.Directory, Path.Combine("Managed", name.Name + ".dll"));
        return File.Exists(local) ? LoadFromAssemblyPath(local) : null;
    }
    protected override nint LoadUnmanagedDll(string name)
    {
        // Explicit per-mod resolution avoids altering the game's process-wide search policy.
        foreach (var folder in new[] { "Native", "Managed" })
        {
            var path = ModCatalog.SafePath(spec.Directory, Path.Combine(folder, name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll"));
            if (File.Exists(path)) return NativeMethods.Load(path);
        }
        return 0;
    }
    private string Validate(string path) => ModCatalog.SafePath(spec.Directory, Path.GetRelativePath(spec.Directory, path));
}
