using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConsGHost.Core;

namespace ConsGHost.Bootstrap;

public static unsafe class Entry
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BootArgs { public uint Size; public char* Config; public nint SetTick; }
    private static RuntimeEngine? _engine;
    internal static nint TickPointer => (nint)(delegate* unmanaged[Cdecl]<double, void>)&Tick;

    [UnmanagedCallersOnly]
    public static int Start(nint arguments, int size)
    {
        string? path = null;
        try
        {
            if (size != sizeof(BootArgs) || arguments == 0) return -1;
            var args = (BootArgs*)arguments;
            if (args->Size != sizeof(BootArgs) || args->Config == null || args->SetTick == 0) return -2;
            path = new string(args->Config);
            var plan = Json.Read<RuntimePlan>(path);
            var setter = args->SetTick;
            _engine = new(plan, p => ((delegate* unmanaged[Cdecl]<nint, void>)setter)(p));
            _engine.InitializeAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception error)
        {
            try { if (path != null) File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "bootstrap-failure.log"), error + Environment.NewLine); } catch { }
            return -3;
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Tick(double delta) { try { _engine?.Tick(delta); } catch { } }
}
