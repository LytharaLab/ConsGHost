using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using ConsGHost.Core;

namespace ConsGHost.Runtime;

internal sealed class Session(string root, HostSettings settings, Func<WireMessage, Task> output) : IAsyncDisposable
{
    private readonly string _pipeName = "cgh-session-" + Guid.NewGuid().ToString("N");
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly ConcurrentDictionary<string, RuntimePeer> _peers = new();
    private readonly List<Process> _children = [];
    private readonly object _childGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<string> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<int> _gameExit = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ProcessJob? _job;
    private List<ModSpec> _mods = [];
    private int _stopping;
    public string Directory { get; } = Path.Combine(root, "Sessions", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
    private TimeSpan Timeout => TimeSpan.FromSeconds(settings.StartupTimeoutSeconds);
    private readonly SemaphoreSlim _journalGate = new(1);
    private async Task Emit(WireMessage message)
    {
        await _journalGate.WaitAsync();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            // Compact NDJSON so each journal entry is independently parseable.
            var compact = new System.Text.Json.JsonSerializerOptions(Json.Options) { WriteIndented = false };
            await File.AppendAllTextAsync(Path.Combine(Directory, "events.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { time = DateTimeOffset.Now, message }, compact) + Environment.NewLine);
        }
        finally { _journalGate.Release(); }
        await output(message);
    }
    private Task Log(string message, string level = "info") => Emit(new() { Kind = "log", Text = message, Level = level });
    public async Task StartAsync()
    {
        try
        {
            _mods = SessionPreparation.Prepare(root, settings);
            _job = new(); System.IO.Directory.CreateDirectory(Directory);
            await Log("Session: " + Directory);
            await Log("Resolved load order: " + string.Join(" -> ", _mods.Select(m => m.Metadata.Id)));
            _ = AcceptLoopAsync();
            _peers["game"] = new("game", _mods.Where(m => !m.Metadata.LoadAsSubprocess).Select(m => m.Metadata.Id).ToHashSet(StringComparer.Ordinal));
            foreach (var mod in _mods.Where(m => m.Metadata.LoadAsSubprocess))
            {
                if (_stopping != 0) throw new OperationCanceledException("Session startup cancelled.");
                var peer = new RuntimePeer(mod.Metadata.Id, [mod.Metadata.Id]); _peers[peer.Endpoint] = peer;
                var config = WritePlan(peer.Endpoint, [mod], isolated: true);
                var worker = Spawn(["--worker", config]);
                _ = WatchWorkerAsync(worker, peer.Endpoint);
                await AwaitHealthy(peer.Loaded.Task, Timeout);
            }
            // The game is created suspended by MRT; native DLL+CLR+mods load before its entry point resumes.
            var gamePlan = WritePlan("game", _mods.Where(m => !m.Metadata.LoadAsSubprocess).ToList(), isolated: false);
            if (_stopping != 0) throw new OperationCanceledException("Session startup cancelled.");
            var args = new List<string> { "--game", Path.Combine(settings.GameDirectory, "gmod_win64.exe"), "--config", gamePlan, "--" };
            args.AddRange(settings.GameArguments);
            var gameLauncher = Spawn(args); _ = WatchGameAsync(gameLauncher);
            await AwaitHealthy(_peers["game"].Loaded.Task, Timeout);
            foreach (var mod in _mods)
            {
                var peer = _peers[mod.Metadata.LoadAsSubprocess ? mod.Metadata.Id : "game"];
                await AwaitHealthy(peer.CommandAsync("start", mod.Metadata.Id, Timeout), Timeout);
            }
            await Emit(new() { Kind = "state", Text = "running", ProcessId = _peers["game"].ProcessId });
            _ = MonitorAsync();
        }
        catch { await StopAsync(); throw; }
    }
    private async Task AwaitHealthy(Task operation, TimeSpan timeout)
    {
        var winner = await Task.WhenAny(operation, _failure.Task, _gameExit.Task, _stopRequested.Task).WaitAsync(timeout, _lifetime.Token);
        if (winner == _stopRequested.Task) throw new OperationCanceledException("Session startup cancelled.");
        if (winner == _failure.Task) throw new InvalidOperationException(await _failure.Task);
        if (winner == _gameExit.Task) throw new InvalidOperationException("Game exited during startup: " + await _gameExit.Task);
        await operation;
    }
    private string WritePlan(string endpoint, List<ModSpec> mods, bool isolated)
    {
        var path = Path.Combine(Directory, endpoint + ".json");
        Json.Write(path, new RuntimePlan
        {
            RootDirectory = root, SessionDirectory = Directory, PipeName = _pipeName, Token = _token, Endpoint = endpoint,
            Mods = mods, ExtraLibraries = isolated ? [] : SessionPreparation.ResolveExtras(settings), IsIsolated = isolated,
            EnableEngineHooks = !isolated && settings.EnableEngineHooks, StartupTimeoutSeconds = settings.StartupTimeoutSeconds, DotnetRoot = settings.DotnetRoot
        });
        return path;
    }
    private Process Spawn(IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(root, "cgh_mrt_win64.exe")) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["CGH_DOTNET_ROOT"] = settings.DotnetRoot;
        Process process;
        lock (_childGate)
        {
            if (_stopping != 0) throw new OperationCanceledException("Session startup cancelled.");
            process = Process.Start(info) ?? throw new IOException("Could not start native MRT.");
            _job!.Assign(process); _children.Add(process);
        }
        _ = PumpAsync(process.StandardOutput, "info"); _ = PumpAsync(process.StandardError, "error");
        return process;
    }
    private async Task PumpAsync(StreamReader reader, string level)
    {
        try { while (await reader.ReadLineAsync(_lifetime.Token) is { } line) await Log(line, level); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    private async Task WatchWorkerAsync(Process worker, string endpoint)
    {
        await worker.WaitForExitAsync();
        if (_stopping == 0) _failure.TrySetResult($"Isolated mod {endpoint} exited unexpectedly ({worker.ExitCode}).");
    }
    private async Task WatchGameAsync(Process launcher)
    {
        await launcher.WaitForExitAsync(); _gameExit.TrySetResult(launcher.ExitCode);
    }
    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 32, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(_lifetime.Token); _ = PeerLoopAsync(new Wire(pipe)); }
                catch { await pipe.DisposeAsync(); throw; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _failure.TrySetResult("IPC accept failed: " + e.Message); }
    }
    private async Task PeerLoopAsync(Wire wire)
    {
        RuntimePeer? peer = null;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); handshake.CancelAfter(Timeout);
            var hello = await wire.ReceiveAsync(handshake.Token);
            if (hello?.Kind != "hello" || hello.Token != _token || hello.Endpoint == null || !_peers.TryGetValue(hello.Endpoint, out peer) || peer.Wire != null)
                throw new InvalidDataException("Rejected runtime handshake.");
            peer.Wire = wire; peer.ProcessId = hello.ProcessId;
            await Log($"Authenticated {peer.Endpoint}, PID {peer.ProcessId}");
            while (await wire.ReceiveAsync(_lifetime.Token) is { } message)
            {
                peer.LastHeartbeat = DateTime.UtcNow;
                switch (message.Kind)
                {
                    case "loaded": peer.Loaded.TrySetResult(); break;
                    case "started": case "stopped-mod": case "stopped":
                        if (peer.Replies.TryGetValue(message.Kind + ":" + message.Mod, out var reply)) reply.TrySetResult();
                        break;
                    case "fatal": _failure.TrySetResult(peer.Endpoint + ": " + message.Text); await Emit(message); break;
                    case "heartbeat": break;
                    case "log": await Emit(message with { Endpoint = peer.Endpoint }); break;
                    case "publish":
                        if (message.Mod == null || !peer.Mods.Contains(message.Mod) || string.IsNullOrWhiteSpace(message.Topic) || message.Topic.Length > 128 || message.Payload?.Length > 256 * 1024)
                            throw new InvalidDataException("Invalid routed message.");
                        var delivery = message with { Kind = "deliver", Token = null, Endpoint = peer.Endpoint };
                        foreach (var target in _peers.Values.Where(p => p.Wire != null && p.Loaded.Task.IsCompletedSuccessfully))
                            await target.Wire!.SendAsync(delivery, _lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5));
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        catch (Exception e) { if (peer != null) _failure.TrySetResult(e.Message); }
        finally
        {
            if (peer != null && _stopping == 0) _failure.TrySetResult("Runtime disconnected: " + peer.Endpoint);
            await wire.DisposeAsync();
        }
    }
    private async Task MonitorAsync()
    {
        try
        {
            while (_stopping == 0)
            {
                var winner = await Task.WhenAny(_failure.Task, _gameExit.Task, Task.Delay(2000, _lifetime.Token));
                if (winner == _failure.Task) { await Log(await _failure.Task, "error"); await output(new() { Kind = "fault", Text = await _failure.Task }); await StopAsync(); return; }
                if (winner == _gameExit.Task) { await Log("Game exited: " + await _gameExit.Task); await StopAsync(); return; }
                foreach (var peer in _peers.Values)
                    if (DateTime.UtcNow - peer.LastHeartbeat > TimeSpan.FromSeconds(15)) _failure.TrySetResult("Runtime heartbeat timed out: " + peer.Endpoint);
            }
        }
        catch (OperationCanceledException) { }
    }
    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) { await _stopFinished.Task; return; }
        _stopRequested.TrySetResult();
        try
        {
            await Emit(new() { Kind = "state", Text = "stopping" });
            foreach (var mod in _mods.AsEnumerable().Reverse())
            {
                var endpoint = mod.Metadata.LoadAsSubprocess ? mod.Metadata.Id : "game";
                if (_peers.TryGetValue(endpoint, out var peer) && peer.Wire != null)
                    try { await peer.CommandAsync("stop-mod", mod.Metadata.Id, TimeSpan.FromSeconds(3)); } catch (Exception e) { await Log("Stop: " + e.Message, "warning"); }
            }
            foreach (var peer in _peers.Values.Where(p => p.Wire != null))
                try { await peer.CommandAsync("stop", null, TimeSpan.FromSeconds(3)); } catch { }
        }
        finally
        {
            Process[] children;
            lock (_childGate) { _job?.Dispose(); children = _children.ToArray(); }
            _lifetime.Cancel();
            foreach (var process in children) { try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { } process.Dispose(); }
            try { await Emit(new() { Kind = "state", Text = "idle" }); }
            finally { _stopFinished.TrySetResult(); }
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
