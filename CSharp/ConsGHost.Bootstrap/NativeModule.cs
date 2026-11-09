using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ConsGHost.SDK;

namespace ConsGHost.Bootstrap;

// Each native module retains its own API table/context for its entire process lifetime.
// DLLs stay mapped: unloading arbitrary native threads or outstanding function pointers is unsafe.
internal sealed unsafe class NativeModule
{
    [StructLayout(LayoutKind.Sequential)]
    private struct HostApi
    {
        public uint Size, Version;
        public nint Context;
        public nint Log, Register, Call, Publish, FindExport, CreateHook, EnableHook, RemoveHook;
        public nint ConfigRead, ConfigSet;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ModInfo
    {
        public uint Size;
        public nint Id, Directory, Resources, Data;
        public int Isolated;
    }
    private readonly nint _handle;
    private readonly ModContext _context;
    private readonly nint _api;
    private readonly nint _info;
    private readonly nint _self;
    private bool _loaded;
    public string Path { get; }
    public NativeModule(string path, ModContext context)
    {
        Path = path; _context = context; _handle = NativeMethods.Load(path);
        _self = GCHandle.ToIntPtr(GCHandle.Alloc(this));
        var api = new HostApi
        {
            Size = (uint)sizeof(HostApi), Version = 1, Context = _self,
            Log = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, byte*, void>)&Log,
            Register = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nint, nint, int>)&Register,
            Call = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, byte*, byte*, int, byte*, int, int>)&Call,
            Publish = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, int>)&Publish,
            FindExport = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, byte*, nint>)&FindExport,
            CreateHook = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, nint*, int>)&CreateHook,
            EnableHook = (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, int>)&EnableHook,
            RemoveHook = (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&RemoveHook,
            ConfigRead = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, int, int>)&ConfigRead,
            ConfigSet = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, byte*, int, int>)&ConfigSet
        };
        _api = Marshal.AllocHGlobal(sizeof(HostApi)); *(HostApi*)_api = api;
        var info = new ModInfo { Size = (uint)sizeof(ModInfo), Id = Utf8(context.Id), Directory = Utf8(context.ModDirectory),
            Resources = Utf8(context.ResourcesDirectory), Data = Utf8(context.DataDirectory), Isolated = context.IsIsolated ? 1 : 0 };
        _info = Marshal.AllocHGlobal(sizeof(ModInfo)); *(ModInfo*)_info = info;
    }
    private static nint Utf8(string s) => Marshal.StringToCoTaskMemUTF8(s);
    private static string Text(byte* p) => Marshal.PtrToStringUTF8((nint)p) ?? "";
    private static NativeModule Self(nint p) => (NativeModule)GCHandle.FromIntPtr(p).Target!;
    private nint Export(string name) => NativeLibrary.TryGetExport(_handle, name, out var address) ? address : 0;
    public nint FindExport(string name) => Export(name);
    public void Load()
    {
        var load = Export("cgh_module_load");
        if (load == 0) return;
        _loaded = true; // Stop is also called after a partially failed Load.
        int rc = ((delegate* unmanaged[Cdecl]<nint, nint, int>)load)(_api, _info);
        if (rc != 0) throw new InvalidOperationException($"{Path}: cgh_module_load returned {rc}");
    }
    public void Start()
    {
        var start = Export("cgh_module_start");
        if (!_loaded || start == 0) return;
        int rc = ((delegate* unmanaged[Cdecl]<int>)start)();
        if (rc != 0) throw new InvalidOperationException($"{Path}: cgh_module_start returned {rc}");
    }
    public void Tick(double delta) { var tick = Export("cgh_module_tick"); if (_loaded && tick != 0) ((delegate* unmanaged[Cdecl]<double, void>)tick)(delta); }
    public void Receive(string source, string topic, byte[] bytes)
    {
        var message = Export("cgh_module_message");
        if (!_loaded || message == 0) return;
        var s = Utf8(source); var t = Utf8(topic);
        try { fixed (byte* data = bytes) ((delegate* unmanaged[Cdecl]<nint, nint, byte*, int, void>)message)(s, t, data, bytes.Length); }
        finally { Marshal.FreeCoTaskMem(s); Marshal.FreeCoTaskMem(t); }
    }
    public void Stop() { if (!_loaded) return; _loaded = false; var stop = Export("cgh_module_stop"); if (stop != 0) ((delegate* unmanaged[Cdecl]<void>)stop)(); }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Log(nint self, byte* level, byte* message) { try { Self(self)._context.Log(Text(message), Text(level)); } catch { } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Register(nint self, byte* name, nint handler, nint user)
    {
        try
        {
            if (handler == 0) return -2;
            Self(self)._context.RegisterService(Text(name), (input, output) =>
            {
                fixed (byte* src = input) fixed (byte* dst = output)
                    return ((delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, int, int>)handler)(user, src, input.Length, dst, output.Length);
            }); return 0;
        } catch { return -2; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Call(nint self, byte* target, byte* service, byte* input, int length, byte* output, int capacity)
    {
        try { if (length < 0 || capacity < 0 || length > 1024 * 1024 || capacity > 1024 * 1024) return -2;
            return Self(self)._context.Call(Text(target), Text(service), new(input, length), new(output, capacity)); } catch { return -2; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Publish(nint self, byte* topic, byte* data, int length)
    {
        try { if (length < 0 || length > 256 * 1024) return -2; Self(self)._context.PublishAsync(Text(topic), new ReadOnlySpan<byte>(data, length).ToArray()).GetAwaiter().GetResult(); return 0; } catch { return -2; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint FindExport(nint self, byte* library, byte* export) { try { return Self(self)._context.FindNativeExport(Text(library), Text(export)); } catch { return 0; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CreateHook(nint self, nint target, nint detour, nint* original)
    { try { if (original == null) return -2; Self(self)._context.CreateHook(target, detour, out *original); return 0; } catch { return -2; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EnableHook(nint self, nint target, int enabled) { try { return Self(self)._context.EnableHook(target, enabled); } catch { return -2; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RemoveHook(nint self, nint target) { try { return Self(self)._context.RemoveHook(target); } catch { return -2; } }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ConfigRead(nint self, byte* output, int capacity)
    {
        try
        {
            if (capacity < 0 || (output == null && capacity != 0)) return -2;
            var config = Self(self)._context.Config;
            if (config == null) return -1;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(config.Read());
            if (bytes.Length <= capacity) bytes.CopyTo(new Span<byte>(output, capacity));
            return bytes.Length;
        }
        catch { return -2; }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ConfigSet(nint self, byte* key, byte* value, int length)
    {
        try
        {
            if (key == null || value == null || length <= 0) return -2;
            var config = Self(self)._context.Config;
            if (config == null) return -1;
            var json = JsonSerializer.Deserialize<JsonElement>(new ReadOnlySpan<byte>(value, length));
            config.Set(Text(key), json);
            return 0;
        }
        catch { return -2; }
    }
}
