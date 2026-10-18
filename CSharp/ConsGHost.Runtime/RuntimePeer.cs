using System.Collections.Concurrent;
using ConsGHost.Core;

namespace ConsGHost.Runtime;

internal sealed class RuntimePeer(string endpoint, HashSet<string> mods)
{
    public string Endpoint { get; } = endpoint;
    public HashSet<string> Mods { get; } = mods;
    public Wire? Wire { get; set; }
    public int ProcessId { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public TaskCompletionSource Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentDictionary<string, TaskCompletionSource> Replies { get; } = new();
    public async Task CommandAsync(string kind, string? mod, TimeSpan timeout)
    {
        var key = (kind == "start" ? "started" : kind == "stop-mod" ? "stopped-mod" : "stopped") + ":" + mod;
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Replies.TryAdd(key, reply)) throw new InvalidOperationException("Command already pending: " + key);
        var connection = Wire ?? throw new IOException("Runtime endpoint is disconnected: " + Endpoint);
        using var deadline = new CancellationTokenSource(timeout);
        try { await connection.SendAsync(new() { Kind = kind, Mod = mod }, deadline.Token); await reply.Task.WaitAsync(deadline.Token); }
        catch
        {
            Wire = null;
            await connection.DisposeAsync();
            throw;
        }
        finally { Replies.TryRemove(key, out _); }
    }
}
