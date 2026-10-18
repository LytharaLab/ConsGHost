namespace ConsGHost.SDK;

/// <summary>Callbacks are serialized per runtime, except explicitly registered services.
/// Engine frames execute on the engine thread; do not block them.</summary>
public interface ICghMod
{
    void Load(IModContext context);
    void Start() { }
    void Tick(double deltaSeconds) { }
    void Stop() { }
}

public delegate int BridgeHandler(ReadOnlySpan<byte> request, Span<byte> response);

public interface IModContext
{
    string Id { get; }
    string ModDirectory { get; }
    string ResourcesDirectory { get; }
    string DataDirectory { get; }
    bool IsIsolated { get; }
    CancellationToken Stopping { get; }
    void Log(string message, string level = "info");
    IDisposable RegisterService(string service, BridgeHandler handler);
    /// <summary>Synchronous, allocation-free dispatch within this process. -1: missing;
    /// -2: failed; -3: output too small. Never call a remote process using this method.</summary>
    int Call(string targetMod, string service, ReadOnlySpan<byte> request, Span<byte> response);
    /// <summary>Bounded asynchronous message routing across runtimes, including isolated mods.</summary>
    ValueTask PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
    IDisposable Subscribe(string topic, Action<string, ReadOnlyMemory<byte>> handler);
    nint FindNativeExport(string libraryName, string exportName);
    /// <summary>Detour and trampoline must use the target's exact native ABI. Keep delegates rooted.
    /// Dispose only after callbacks have quiesced; hooks are disabled automatically during shutdown.</summary>
    IDisposable CreateHook(nint target, nint detour, out nint trampoline);
    /// <summary>Runs on the next actual engine frame. Isolated mods do not have engine access.</summary>
    void RunOnGameThread(Action action);
}
