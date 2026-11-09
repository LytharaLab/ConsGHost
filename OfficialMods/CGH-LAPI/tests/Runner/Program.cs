using System.Collections.Concurrent;
using ConsGHost.Core;

if (args.Length != 2) throw new ArgumentException("Expected test CGH distribution and GMod directory.");
var distribution = Path.GetFullPath(args[0]);
Json.Write(Path.Combine(distribution, "cgh_settings.json"), new HostSettings
{
    GameDirectory = Path.GetFullPath(args[1]),
    GameArguments = ["-console", "-novid", "-insecure", "-noaddons", "-noworkshop",
        "-windowed", "-w", "640", "-h", "480", "+map", "gm_construct"],
    EnableEngineHooks = false,
    StartupTimeoutSeconds = 120
});

var messages = new ConcurrentQueue<WireMessage>();
await using var client = new RuntimeClient(distribution);
client.Message += message =>
{
    messages.Enqueue(message);
    if (message.Kind is not "heartbeat") Console.WriteLine($"{message.Kind} {message.Endpoint} {message.Mod}: {message.Text}");
};

async Task WaitFor(Func<WireMessage[], bool> predicate, string stage, int seconds)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    while (!timeout.IsCancellationRequested)
    {
        var snapshot = messages.ToArray();
        var failure = snapshot.FirstOrDefault(m => m.Kind is "fault" ||
            m.Text?.StartsWith("LAPI_TEST_FAIL ", StringComparison.Ordinal) == true);
        if (failure != null) throw new InvalidOperationException(failure.Text);
        if (predicate(snapshot)) return;
        try { await Task.Delay(100, timeout.Token); }
        catch (OperationCanceledException) { break; }
    }
    throw new TimeoutException(stage);
}

await client.ConnectAsync();
await WaitFor(m => m.Any(x => x.Kind == "state" && x.Text == "idle"), "CGH handshake", 20);
await client.SendAsync("launch");
await WaitFor(m => m.Any(x => x.Kind == "state" && x.Text == "running"), "GMod startup", 150);
await WaitFor(m => m.Any(x => x.Mod == "cgh.lapi.test" && x.Text == "LAPI_TEST_PASS server") &&
    m.Any(x => x.Mod == "cgh.lapi.test" && x.Text == "LAPI_TEST_PASS client"), "GLua <-> CGH smoke tests", 120);
await client.SendAsync("stop");
await WaitFor(m => m.Count(x => x.Kind == "state" && x.Text == "idle") >= 2, "CGH shutdown", 30);
Console.WriteLine("CGH-LAPI real-game smoke tests passed in server and client realms.");
