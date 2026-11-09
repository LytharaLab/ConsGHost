using System.Diagnostics;
using System.Reflection;

namespace ConsGHost.Core;

public static class CghVersion
{
    public static string Current { get; } = typeof(CghVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private static readonly string[] Components =
    [
        "ConsGHost.Host.exe", "ConsGHost.Host.dll",
        "ConsGHost.Runtime.exe", "ConsGHost.Runtime.dll",
        "ConsGHost.Core.dll", "ConsGHost.Bootstrap.dll", "ConsGHost.SDK.dll",
        "cgh_mrt_win64.exe", "cgh_runtime_win64.dll"
    ];

    public static void VerifyDistribution(string root)
    {
        foreach (var component in Components)
        {
            var path = Path.Combine(root, component);
            if (!File.Exists(path)) throw new FileNotFoundException("Incomplete CGH distribution: " + component, path);
            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            if (version != Current)
                throw new InvalidDataException($"CGH component version mismatch: {component} is {version ?? "unversioned"}, expected {Current}.");
        }
    }
}
