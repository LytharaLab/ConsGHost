using System.Collections.Concurrent;
using ConsGHost.Core;
using ConsGHost.SDK;

namespace ConsGHost.Bootstrap;

internal sealed class ModContext(RuntimeEngine engine, ModSpec spec) : IModContext, IDisposable
{
    public string Id => spec.Metadata.Id;
    public string ModDirectory => spec.Directory;
    public string ResourcesDirectory => Path.Combine(spec.Directory, "Resources");
    public string DataDirectory { get; } = Directory.CreateDirectory(Path.Combine(engine.Plan.RootDirectory, "Data", spec.Metadata.Id)).FullName;
    public bool IsIsolated => engine.Plan.IsIsolated;
    public CancellationToken Stopping => _stopping.Token;
    public IModConfig? Config { get; } = ModConfig.Open(spec.Directory);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentBag<IDisposable> _registrations = [];
    private readonly ConcurrentDictionary<nint, IDisposable> _hooks = [];
    private readonly ConcurrentDictionary<string, Action<string, ReadOnlyMemory<byte>>> _subscribers = new(StringComparer.Ordinal);
    public void Log(string message, string level = "info") => engine.Log(message, level, Id);
    public IDisposable RegisterService(string service, BridgeHandler handler)
    {
        Stopping.ThrowIfCancellationRequested();
        var registration = engine.Bridge.Register(Id, service, handler); _registrations.Add(registration); return registration;
    }
    public int Call(string targetMod, string service, ReadOnlySpan<byte> request, Span<byte> response) =>
        Stopping.IsCancellationRequested ? -2 : engine.Bridge.Call(targetMod, service, request, response);
    public ValueTask PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Stopping.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 128 || payload.Length > 256 * 1024) throw new ArgumentException("Invalid topic or payload > 256 KiB.");
        if (!engine.Queue(new() { Kind = "publish", Mod = Id, Topic = topic, Payload = payload.ToArray() }))
            throw new IOException("Runtime IPC output queue is full.");
        return ValueTask.CompletedTask;
    }
    public IDisposable Subscribe(string topic, Action<string, ReadOnlyMemory<byte>> handler)
    {
        if (!_subscribers.TryAdd(topic, handler)) throw new InvalidOperationException("This mod already subscribes to " + topic);
        var registration = new Subscription(() => _subscribers.TryRemove(topic, out _)); _registrations.Add(registration); return registration;
    }
    internal void Receive(WireMessage message)
    {
        if (message.Topic != null && _subscribers.TryGetValue(message.Topic, out var handler)) handler(message.Mod ?? "", message.Payload ?? []);
    }
    public nint FindNativeExport(string libraryName, string exportName) => engine.FindExport(Id, libraryName, exportName);
    public IDisposable CreateHook(nint target, nint detour, out nint trampoline)
    {
        Stopping.ThrowIfCancellationRequested();
        var rc = NativeMethods.CreateHook(target, detour, out trampoline);
        if (rc != 0) throw new InvalidOperationException($"MinHook create failed: {rc}");
        var hook = new Subscription(() => { NativeMethods.EnableHook(target, 0); NativeMethods.RemoveHook(target); _hooks.TryRemove(target, out _); });
        _hooks[target] = hook;
        rc = NativeMethods.EnableHook(target, 1);
        if (rc != 0) { hook.Dispose(); throw new InvalidOperationException($"MinHook enable failed: {rc}"); }
        return hook;
    }
    internal void DisableHooks() { foreach (var target in _hooks.Keys) NativeMethods.EnableHook(target, 0); }
    internal int EnableHook(nint target, int enabled) => _hooks.ContainsKey(target) ? NativeMethods.EnableHook(target, enabled) : -1;
    internal int RemoveHook(nint target) { if (!_hooks.TryGetValue(target, out var hook)) return -1; hook.Dispose(); return 0; }
    public void RunOnGameThread(Action action)
    {
        if (IsIsolated) throw new InvalidOperationException("Isolated mod has no game-thread access; publish a message to an in-process mod.");
        Stopping.ThrowIfCancellationRequested(); engine.Post(action);
    }
    public void Dispose()
    {
        _stopping.Cancel(); DisableHooks();
        foreach (var hook in _hooks.Values) hook.Dispose();
        foreach (var registration in _registrations) registration.Dispose();
        _subscribers.Clear();
    }
}
