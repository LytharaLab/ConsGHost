using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ConsGHost.SDK;

namespace ConsGHost.Examples;

public sealed class BridgeDemo : ICghMod
{
    private IModContext _context = null!;
    private long _ticks;
    public void Load(IModContext context)
    {
        _context = context;
        context.RegisterService("managed.echo", (input, output) =>
        {
            if (output.Length < input.Length) return -3;
            input.CopyTo(output); return input.Length;
        });
        context.Subscribe("observer.ready", (_, data) =>
        {
            context.Log("Cross-process observer handshake: " + Encoding.UTF8.GetString(data.Span));
            context.PublishAsync("example.ping", Encoding.UTF8.GetBytes("hello from game")).GetAwaiter().GetResult();
        });
        context.Subscribe("observer.ack", (_, _) => context.Log("Cross-process game -> isolated -> game round trip passed"));
    }
    public void Start()
    {
        ReadOnlySpan<byte> input = "managed -> native bridge OK"u8;
        Span<byte> output = stackalloc byte[128];
        int size = _context.Call(_context.Id, "native.echo", input, output);
        if (size != input.Length || !input.SequenceEqual(output[..size])) throw new InvalidOperationException("Native echo bridge failed.");
        _context.Log("Managed -> native direct service call passed");
        const int iterations = 100000;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; ++i)
            if (_context.Call(_context.Id, "native.echo", input, output) != input.Length) throw new InvalidOperationException("Bridge stress failed.");
        watch.Stop();
        var metrics = new { processId = Environment.ProcessId, iterations, payloadBytes = input.Length, milliseconds = watch.Elapsed.TotalMilliseconds,
            nanosecondsPerCall = watch.Elapsed.TotalNanoseconds / iterations, isolated = _context.IsIsolated };
        File.WriteAllText(Path.Combine(_context.DataDirectory, "bridge-metrics.json"), JsonSerializer.Serialize(metrics));
        _context.Log($"Bridge stress passed: {iterations:N0} direct calls, {watch.Elapsed.TotalMilliseconds:F2} ms (local measurement)");
        if (!_context.IsIsolated) _context.RunOnGameThread(() => _context.Log("Game-thread dispatch passed"));
    }
    public void Tick(double deltaSeconds)
    {
        if (++_ticks == 1) { _context.Log(_context.IsIsolated ? "Managed Tick received worker timer" : "Managed Tick received actual engine frame"); File.WriteAllText(Path.Combine(_context.DataDirectory, "first-frame.txt"), $"{Environment.ProcessId} {DateTimeOffset.Now:O}"); }
        if (_ticks == 300) _context.Log(_context.IsIsolated ? "Worker stability check passed: 300 ticks" : "Engine stability check passed: 300 frames");
    }
    public void Stop() => _context.Log($"Managed module stopped after {_ticks} frame(s)");
}
