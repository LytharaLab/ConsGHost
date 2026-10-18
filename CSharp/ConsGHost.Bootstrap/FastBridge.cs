using System.Collections.Concurrent;
using ConsGHost.SDK;

namespace ConsGHost.Bootstrap;

internal sealed class Subscription(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

internal sealed class FastBridge
{
    private readonly ConcurrentDictionary<(string Mod, string Service), BridgeHandler> _services = new();
    public IDisposable Register(string mod, string name, BridgeHandler handler)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) throw new ArgumentException("Service name is empty or too long.");
        var key = (mod, name);
        if (!_services.TryAdd(key, handler)) throw new InvalidOperationException($"Duplicate service {mod}/{name}");
        return new Subscription(() => _services.TryRemove(key, out _));
    }
    [ThreadStatic] private static int _depth;
    public int Call(string mod, string service, ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (!_services.TryGetValue((mod, service), out var handler)) return -1;
        if (_depth >= 32) return -2;
        try { ++_depth; var size = handler(input, output); return size > output.Length ? -3 : size; }
        catch { return -2; }
        finally { --_depth; }
    }
}
