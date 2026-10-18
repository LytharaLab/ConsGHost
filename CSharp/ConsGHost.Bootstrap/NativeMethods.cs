using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ConsGHost.Bootstrap;

internal static partial class NativeMethods
{
    [LibraryImport("kernel32", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint LoadLibraryEx(string path, nint file, uint flags);
    [LibraryImport("kernel32", EntryPoint = "AddDllDirectory", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint AddDllDirectory(string path);
    [LibraryImport("kernel32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveDllDirectory(nint cookie);
    [LibraryImport("cgh_runtime_win64", EntryPoint = "cgh_attach_engine")]
    internal static partial int AttachEngine();
    [LibraryImport("cgh_runtime_win64", EntryPoint = "cgh_hook_create")]
    internal static partial int CreateHook(nint target, nint detour, out nint original);
    [LibraryImport("cgh_runtime_win64", EntryPoint = "cgh_hook_enable")]
    internal static partial int EnableHook(nint target, int enabled);
    [LibraryImport("cgh_runtime_win64", EntryPoint = "cgh_hook_remove")]
    internal static partial int RemoveHook(nint target);
    internal static nint Load(string path)
    {
        var handle = LoadLibraryEx(path, 0, 0x100 | 0x1000 | 0x400); // DLL_LOAD_DIR | DEFAULT_DIRS | USER_DIRS
        return handle != 0 ? handle : throw new Win32Exception(Marshal.GetLastWin32Error(), $"Load DLL: {path}");
    }
}
