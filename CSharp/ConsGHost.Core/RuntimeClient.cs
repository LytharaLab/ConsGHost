using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;

namespace ConsGHost.Core;

public sealed class RuntimeClient(string root) : IAsyncDisposable
{
    private Process? _process;
    private ProcessJob? _job;
    private Wire? _wire;
    private Task? _reader;
    private bool _disposing;
    public event Action<WireMessage>? Message;
    public async Task ConnectAsync()
    {
        CghVersion.VerifyDistribution(root);
        var name = "cgh-host-" + Guid.NewGuid().ToString("N");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(Path.Combine(root, "ConsGHost.Runtime.exe")) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--serve", name, token, root }) start.ArgumentList.Add(arg);
        _job = new();
        _process = Process.Start(start) ?? throw new IOException("Could not start CGH Runtime."); _job.Assign(_process);
        var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(15000); }
        catch { await pipe.DisposeAsync(); _job.Dispose(); throw; }
        _wire = new(pipe);
        await _wire.SendAsync(new() { Kind = "hello", Token = token });
        _reader = ReadAsync();
    }
    private async Task ReadAsync()
    {
        try { while (await _wire!.ReceiveAsync() is { } message) Message?.Invoke(message); }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        { if (!_disposing) Message?.Invoke(new() { Kind = "fault", Text = "Runtime connection lost: " + error.Message }); }
        finally { if (!_disposing) Message?.Invoke(new() { Kind = "state", Text = "disconnected" }); }
    }
    public Task SendAsync(string kind) => _wire?.SendAsync(new() { Kind = kind }) ?? throw new InvalidOperationException("Runtime is disconnected.");
    public async ValueTask DisposeAsync()
    {
        if (_disposing) return; _disposing = true;
        if (_wire != null)
        {
            try { await _wire.SendAsync(new() { Kind = "shutdown" }).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
        }
        if (_process != null) try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(12)); } catch { }
        _job?.Dispose();
        if (_wire != null) await _wire.DisposeAsync();
        if (_reader != null) try { await _reader; } catch { }
        _process?.Dispose();
    }
}
