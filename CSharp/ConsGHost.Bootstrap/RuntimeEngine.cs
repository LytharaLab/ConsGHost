using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ConsGHost.Core;
using ConsGHost.SDK;

namespace ConsGHost.Bootstrap;

internal sealed class LoadedMod(ModSpec spec, ModContext context)
{
    public ModSpec Spec { get; } = spec;
    public ModContext Context { get; } = context;
    public ModLoadContext? AssemblyContext { get; set; }
    public List<ICghMod> Managed { get; } = [];
    public List<NativeModule> Native { get; } = [];
    public bool Started { get; set; }
    public bool Stopped { get; set; }
}

internal sealed class RuntimeEngine(RuntimePlan plan, Action<nint> setTick)
{
    public RuntimePlan Plan { get; } = plan;
    public FastBridge Bridge { get; } = new();
    private readonly object _gate = new();
    private readonly List<LoadedMod> _mods = [];
    private readonly List<nint> _dllDirectories = [];
    private readonly List<nint> _extras = [];
    private readonly ConcurrentQueue<Action> _gameQueue = new();
    private readonly Channel<WireMessage> _outgoing = Channel.CreateBounded<WireMessage>(new BoundedChannelOptions(2048) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private Wire? _wire;
    private long _frames;
    private int _failed, _stopped;
    private Task? _sender;

    public bool Queue(WireMessage message) => _outgoing.Writer.TryWrite(message);
    public void Log(string message, string level = "info", string? mod = null)
    {
        Queue(new() { Kind = "log", Text = message.Length > 8192 ? message[..8192] : message, Level = level, Mod = mod });
    }
    public void Post(Action action)
    {
        if (_gameQueue.Count >= 4096) throw new InvalidOperationException("Game-thread queue is full.");
        _gameQueue.Enqueue(action);
    }
    public nint FindExport(string mod, string library, string symbol)
    {
        var loaded = _mods.FirstOrDefault(m => m.Spec.Metadata.Id == mod);
        return loaded?.Native.FirstOrDefault(n => Path.GetFileName(n.Path).Equals(library, StringComparison.OrdinalIgnoreCase))?.FindExport(symbol) ?? 0;
    }
    public async Task InitializeAsync()
    {
        var client = new NamedPipeClientStream(".", Plan.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(Plan.StartupTimeoutSeconds * 1000);
        _wire = new Wire(client);
        await _wire.SendAsync(new() { Kind = "hello", Token = Plan.Token, Endpoint = Plan.Endpoint, ProcessId = Environment.ProcessId });
        _sender = SendLoopAsync();
        try
        {
            foreach (var dir in Plan.ExtraLibraries.Select(Path.GetDirectoryName).Concat(Plan.Mods.Select(m => Path.Combine(m.Directory, "Native")))
                         .Where(d => d != null && Directory.Exists(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var cookie = NativeMethods.AddDllDirectory(dir!);
                if (cookie == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"AddDllDirectory: {dir}");
                _dllDirectories.Add(cookie);
            }
            foreach (var extra in Plan.ExtraLibraries)
            {
                _extras.Add(NativeMethods.Load(extra)); Log("Loaded extra DLL: " + extra);
                if (Path.GetFileName(extra).Equals("cgh_test_extra.dll", StringComparison.OrdinalIgnoreCase))
                    Log("Extra dependency fixture loaded");
            }
            foreach (var spec in Plan.Mods)
            {
                var context = new ModContext(this, spec); var mod = new LoadedMod(spec, context); _mods.Add(mod);
                // Map native modules first, then managed Load, then native Load: both sides can register services.
                foreach (var library in spec.Native) mod.Native.Add(new(library, context));
                if (spec.Managed.Count > 0)
                {
                    mod.AssemblyContext = new(spec);
                    foreach (var assemblyPath in spec.Managed)
                    {
                        var assembly = mod.AssemblyContext.LoadFromAssemblyPath(assemblyPath);
                        foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && typeof(ICghMod).IsAssignableFrom(t)).OrderBy(t => t.FullName))
                        {
                            var entry = (ICghMod)(Activator.CreateInstance(type) ?? throw new InvalidDataException("No public parameterless constructor: " + type));
                            mod.Managed.Add(entry); entry.Load(context);
                        }
                    }
                }
                foreach (var native in mod.Native) native.Load();
                Log($"Loaded {spec.Metadata.Id} {spec.Metadata.Version}: {mod.Managed.Count} managed entry point(s), {mod.Native.Count} native DLL(s)");
            }
            setTick(Entry.TickPointer);
            await _wire.SendAsync(new() { Kind = "loaded", Endpoint = Plan.Endpoint, ProcessId = Environment.ProcessId });
            _ = ControlLoopAsync();
            _ = HeartbeatLoopAsync();
            if (Plan.IsIsolated) _ = WorkerTickLoopAsync();
            else if (Plan.EnableEngineHooks) _ = AttachEngineLoopAsync();
        }
        catch (Exception error)
        {
            await FailAndStopAsync(error);
            throw;
        }
    }
    private async Task SendLoopAsync()
    {
        try { await foreach (var message in _outgoing.Reader.ReadAllAsync()) await _wire!.SendAsync(message).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException) { Stop(); }
    }
    private async Task ControlLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested && await _wire!.ReceiveAsync(_lifetime.Token) is { } message)
            {
                switch (message.Kind)
                {
                    case "start":
                        lock (_gate)
                        {
                            var mod = _mods.Single(m => m.Spec.Metadata.Id == message.Mod);
                            if (!mod.Started && !mod.Stopped)
                            {
                                foreach (var managed in mod.Managed) managed.Start();
                                foreach (var native in mod.Native) native.Start();
                                mod.Started = true; Log("Started", mod: mod.Spec.Metadata.Id);
                            }
                        }
                        await _wire.SendAsync(new() { Kind = "started", Mod = message.Mod });
                        break;
                    case "stop-mod":
                        lock (_gate) StopMod(_mods.Single(m => m.Spec.Metadata.Id == message.Mod));
                        await _wire.SendAsync(new() { Kind = "stopped-mod", Mod = message.Mod });
                        break;
                    case "deliver":
                        lock (_gate)
                            foreach (var mod in _mods.Where(m => m.Started && !m.Stopped))
                            {
                                mod.Context.Receive(message);
                                foreach (var native in mod.Native) native.Receive(message.Mod ?? "", message.Topic ?? "", message.Payload ?? []);
                            }
                        break;
                    case "stop":
                        Stop();
                        if (_sender != null) await _sender.WaitAsync(TimeSpan.FromSeconds(2));
                        await _wire.SendAsync(new() { Kind = "stopped" });
                        await _wire.DisposeAsync();
                        if (Plan.IsIsolated) Environment.Exit(0);
                        return;
                }
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
        catch (Exception error)
        {
            await FailAndStopAsync(error);
        }
        finally
        {
            Stop();
            try { if (_sender != null) await _sender.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            try { if (_wire != null) await _wire.DisposeAsync(); } catch { }
            if (Plan.IsIsolated) Environment.Exit(_failed != 0 ? 1 : 0);
        }
    }
    public void Tick(double delta)
    {
        if (Volatile.Read(ref _stopped) != 0 || !Monitor.TryEnter(_gate)) return;
        try
        {
            if (Interlocked.Increment(ref _frames) == 1) Log(Plan.IsIsolated ? "Worker tick active" : "Engine GameFrame callback active");
            for (int count = 0; count < 256 && _gameQueue.TryDequeue(out var action); ++count) action();
            foreach (var mod in _mods)
            {
                if (!mod.Started || mod.Stopped) continue;
                foreach (var entry in mod.Managed) entry.Tick(delta);
                foreach (var native in mod.Native) native.Tick(delta);
            }
        }
        catch (Exception error) { Fault(error); }
        finally { Monitor.Exit(_gate); }
    }
    private void Fault(Exception error)
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0) Queue(new() { Kind = "fatal", Text = error.ToString() });
        setTick(0);
    }
    private async Task FailAndStopAsync(Exception error)
    {
        Interlocked.Exchange(ref _failed, 1);
        // Complete cleanup before the coordinator can tear down our process on failure.
        Stop();
        try { if (_sender != null) await _sender.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        try { await _wire!.SendAsync(new() { Kind = "fatal", Text = error.ToString() }).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }
    private async Task AttachEngineLoopAsync()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                int result = NativeMethods.AttachEngine();
                if (result == 1) { Log("Hook attached: ServerGameDLL009::GameFrame"); return; }
                if (result < 0) { Fault(new NotSupportedException($"Engine Hook unavailable ({result}); unsupported engine interface or detour.")); return; }
                await Task.Delay(250, _lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Fault(error); }
    }
    private async Task WorkerTickLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            var watch = Stopwatch.StartNew(); var last = watch.Elapsed;
            while (await timer.WaitForNextTickAsync(_lifetime.Token)) { var now = watch.Elapsed; Tick((now - last).TotalSeconds); last = now; }
        }
        catch (OperationCanceledException) { }
    }
    private async Task HeartbeatLoopAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(_lifetime.Token)) Queue(new() { Kind = "heartbeat", Text = Interlocked.Read(ref _frames).ToString() });
        }
        catch (OperationCanceledException) { }
    }
    private void StopMod(LoadedMod mod)
    {
        if (mod.Stopped) return;
        mod.Stopped = true; mod.Context.DisableHooks();
        foreach (var native in mod.Native.AsEnumerable().Reverse()) try { native.Stop(); } catch (Exception e) { Log(e.ToString(), "error", mod.Spec.Metadata.Id); }
        foreach (var managed in mod.Managed.AsEnumerable().Reverse()) try { managed.Stop(); } catch (Exception e) { Log(e.ToString(), "error", mod.Spec.Metadata.Id); }
        mod.Context.Dispose(); mod.Managed.Clear(); mod.AssemblyContext?.Unload(); mod.AssemblyContext = null;
        Log("Stopped", mod: mod.Spec.Metadata.Id);
    }
    private void Stop()
    {
        setTick(0);
        lock (_gate)
        {
            if (_stopped != 0) return;
            Volatile.Write(ref _stopped, 1);
            foreach (var mod in _mods.AsEnumerable().Reverse()) StopMod(mod);
            foreach (var cookie in _dllDirectories) NativeMethods.RemoveDllDirectory(cookie);
            _outgoing.Writer.TryComplete(); _lifetime.Cancel();
        }
    }
}
