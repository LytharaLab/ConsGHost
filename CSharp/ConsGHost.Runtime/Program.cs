using System.IO.Pipes;
using ConsGHost.Core;
using ConsGHost.Runtime;

if (args.Length == 2 && args[0] == "--validate")
{
    try
    {
        var root = Path.GetFullPath(args[1]);
        var settings = Json.Read<HostSettings>(Path.Combine(root, "cgh_settings.json"));
        var mods = SessionPreparation.Prepare(root, settings);
        Console.WriteLine("VALID: " + string.Join(" -> ", mods.Select(m => m.Metadata.Id))); return 0;
    }
    catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
}
if (args.Length != 4 || args[0] != "--serve")
{
    Console.Error.WriteLine("ConsGHost.Runtime --serve PIPE TOKEN ROOT | --validate ROOT"); return 2;
}
var distribution = Path.GetFullPath(args[3]);
await using var pipe = new NamedPipeServerStream(args[1], PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await pipe.WaitForConnectionAsync(connectTimeout.Token);
await using var wire = new Wire(pipe);
var hello = await wire.ReceiveAsync(connectTimeout.Token);
if (hello?.Kind != "hello" || hello.Token != args[2]) return 3;
await wire.SendAsync(new() { Kind = "state", Text = "idle" });
Session? session = null;
Task? launching = null;
try
{
    while (await wire.ReceiveAsync() is { } message)
    {
        switch (message.Kind)
        {
            case "launch":
                if (launching is { IsCompleted: false }) break;
                if (session != null) await session.StopAsync();
                var settings = Json.Read<HostSettings>(Path.Combine(distribution, "cgh_settings.json"));
                session = new(distribution, settings, m => wire.SendAsync(m));
                await wire.SendAsync(new() { Kind = "state", Text = "starting" });
                launching = LaunchAsync(session, wire);
                break;
            case "stop":
                if (session != null) await session.StopAsync();
                if (launching != null) try { await launching; } catch { }
                break;
            case "shutdown": return 0;
        }
    }
}
catch (Exception e) when (e is IOException or OperationCanceledException) { }
finally { if (session != null) await session.StopAsync(); }
return 0;

static async Task LaunchAsync(Session session, Wire wire)
{
    try { await session.StartAsync(); }
    catch (OperationCanceledException) { }
    catch (Exception error)
    {
        try { await wire.SendAsync(new() { Kind = "fault", Text = error.Message }); await wire.SendAsync(new() { Kind = "state", Text = "idle" }); } catch (IOException) { }
    }
}
