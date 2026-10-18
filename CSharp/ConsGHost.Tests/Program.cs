using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ConsGHost.Core;

var repo = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(AppContext.BaseDirectory, "../../../../../"));
bool real = args.Contains("--real-game");
var output = Path.Combine(repo, "artifacts", "tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); ++passed; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is InvalidDataException or ArgumentException or BadImageFormatException) { Check(true, name); return; } throw new Exception("FAIL expected rejection: " + name); }
ModSpec Mod(string id, params ModDependency[] dependencies) => new(new() { Id = id, Name = id, Version = "1.0.0", Dependencies = dependencies.ToList() }, output, [], []);
try
{
    var a = Mod("a"); var b = Mod("b", new ModDependency() { Id = "a", MinVersion = "1.0.0", MaxVersionExclusive = "2.0.0" });
    Check(ModCatalog.Resolve([b, a], []).Select(m => m.Metadata.Id).SequenceEqual(["a", "b"]), "stable dependency order");
    Reject(() => ModCatalog.Resolve([a, b], ["a"]), "disabled required dependency");
    Reject(() => ModCatalog.Resolve([a, a], []), "duplicate ids");
    Reject(() => ModCatalog.Resolve([Mod("a", new ModDependency() { Id = "b" }), b], []), "dependency cycle");
    Reject(() => ModCatalog.Resolve([a, Mod("b", new ModDependency() { Id = "a", MinVersion = "2.0.0" })], []), "version constraint");
    Check(ModCatalog.Resolve([Mod("a", new ModDependency() { Id = "absent", Optional = true })], []).Count == 1, "missing optional dependency");
    Reject(() => ModCatalog.SafePath(output, "../escape.dll"), "path traversal");
    Reject(() => ModCatalog.ParseVersion("1.0"), "non-three-component version");
    var invalid = Path.Combine(output, "catalog", "Invalid"); Directory.CreateDirectory(invalid);
    File.WriteAllText(Path.Combine(invalid, "cghm_metadata.json"), "{\"id\":\"bad\",\"name\":\"bad\",\"version\":\"1.0.0\",\"typoField\":true}");
    Check(ModCatalog.Scan(Path.GetDirectoryName(invalid)!).Issues.Count == 1, "unknown metadata field diagnosed");
    await using (var memory = new MemoryStream())
    {
        await using var writer = new Wire(memory);
        await writer.SendAsync(new() { Kind = "publish", Topic = "test", Payload = Encoding.UTF8.GetBytes("双语言") }); memory.Position = 0;
        Check((await writer.ReceiveAsync())?.Payload is { } p && Encoding.UTF8.GetString(p) == "双语言", "framed IPC UTF-8 round trip");
    }
    await using (var malformed = new Wire(new MemoryStream([255, 255, 255, 127])))
    {
        try { await malformed.ReceiveAsync(); throw new Exception("Missing frame limit"); }
        catch (InvalidDataException) { Check(true, "oversized IPC rejected before allocation"); }
    }
    Reject(() => ModCatalog.ValidateX64(typeof(ModSpec).Assembly.Location, requireNativeDll: true), "managed assembly rejected as native DLL");
    var distribution = Path.Combine(output, "CGH space 中文");
    CopyTree(Path.Combine(repo, "artifacts", "CGH"), distribution);
    var game = Path.Combine(output, "fixture-game"); Directory.CreateDirectory(game);
    var extra = Path.Combine(output, "extras"); Directory.CreateDirectory(extra);
    File.Copy(Path.Combine(repo, "Cpp/build/Release/cgh_test_game.exe"), Path.Combine(game, "gmod_win64.exe"));
    File.Copy(Path.Combine(repo, "Cpp/build/Release/server.dll"), Path.Combine(game, "server.dll"));
    File.Copy(Path.Combine(repo, "Cpp/build/Release/cgh_test_extra.dll"), Path.Combine(extra, "cgh_test_extra.dll"));
    var settings = new HostSettings { GameDirectory = game, GameArguments = ["45"], ExtraLibraries = [new() { Path = extra }] };
    if (real) { settings.GameDirectory = @"L:\SteamLibrary\steamapps\common\GarrysMod"; settings.GameArguments = ["-console", "-novid", "-insecure", "-noaddons", "-noworkshop", "-windowed", "-w", "1024", "-h", "768", "+map", "gm_construct"]; settings.StartupTimeoutSeconds = 120; }
    Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
    var messages = new ConcurrentQueue<WireMessage>();
    await using var client = new RuntimeClient(distribution);
    client.Message += m => { messages.Enqueue(m); if (m.Kind != "heartbeat") Console.WriteLine($"  {m.Kind} {m.Endpoint} {m.Mod}: {m.Text}"); };
    await client.ConnectAsync();
    await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "runtime handshake", 20);
    await client.SendAsync("launch");
    await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "running"), "controlled process launch", 150, messages);
    Check(true, real ? "real GMod bootstrapped and mod startup acknowledged" : "fixture game bootstrapped before entry and mod startup acknowledged");
    await Until(() => messages.Any(m => m.Text?.Contains("round trip passed") == true), "cross-process bridge", 20, messages);
    Check(true, "game and isolated mod message round trip");
    await Until(() => messages.Any(m => m.Text?.Contains("Engine GameFrame callback active") == true), "engine frames", real ? 150 : 20, messages);
    Check(true, real ? "real GMod engine GameFrame detour" : "synthetic Source interface GameFrame detour");
    await Until(() => messages.Any(m => m.Text == "Engine stability check passed: 300 frames"), "continuous engine frames", real ? 300 : 20, messages);
    Check(true, real ? "300 consecutive real GMod engine callbacks" : "300 consecutive synthetic engine callbacks");
    if (real && args.Contains("--hold")) { Console.WriteLine("Holding real game for window inspection (30 seconds)"); await Task.Delay(30000); }
    await Until(() => messages.Any(m => m.Text?.Contains("Game-thread dispatch passed") == true), "game-thread dispatch", 20, messages);
    Check(messages.Any(m => m.Text?.Contains("Native -> managed direct service call passed") == true), "native to C# service");
    Check(messages.Any(m => m.Text?.Contains("Managed -> native direct service call passed") == true), "C# to native service");
    Check(messages.Any(m => m.Text?.Contains("Extra dependency fixture loaded") == true), "extra DLL loaded in game runtime");
    if (!real) Check(messages.Any(m => m.Text == "CGH_EXTRA_PROBE=42"), "game can invoke extra dependency export");
    var metrics = Path.Combine(distribution, "Data/cgh.example/bridge-metrics.json"); Check(File.Exists(metrics), "100000-call bridge stress completed");
    Console.WriteLine("  " + File.ReadAllText(metrics));
    await client.SendAsync("stop");
    await Until(() => messages.Count(m => m.Kind == "state" && m.Text == "idle") >= 2, "session shutdown", 40);
    Check(messages.Any(m => m.Text?.StartsWith("Managed module stopped") == true), "managed graceful stop callback");
    Check(messages.Any(m => m.Text == "Native module stopped"), "native graceful stop callback");
    Check(messages.Any(m => m.Text == "Isolated observer stopped"), "isolated graceful stop callback");
    var started = messages.First(m => m.Kind == "state" && m.Text == "running");
    Check(!IsAlive(started.ProcessId), "owned game process cleaned up");
    if (!real)
    {
        messages.Clear(); await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "running"), "second session", 90, messages);
        Check(true, "relaunch after stop");
        var auth = messages.First(m => m.Text?.StartsWith("Authenticated cgh.observer, PID ") == true).Text!;
        int pid = int.Parse(auth[(auth.LastIndexOf(' ') + 1)..]);
        using (var worker = Process.GetProcessById(pid)) worker.Kill();
        await Until(() => messages.Any(m => m.Kind == "fault"), "isolated crash detection", 20);
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "crash rollback", 40);
        Check(true, "isolated mod crash detected and session rolled back");
        Check(!IsAlive(messages.First(m => m.Kind == "state" && m.Text == "running").ProcessId), "crash rollback removed game");

        messages.Clear();
        var failing = Path.Combine(distribution, "Mods", "Failure"); Directory.CreateDirectory(Path.Combine(failing, "Managed"));
        File.Copy(Path.Combine(repo, "CSharp/ConsGHost.TestMod/bin/Release/net10.0-windows/ConsGHost.TestMod.dll"), Path.Combine(failing, "Managed/ConsGHost.TestMod.dll"));
        Json.Write(Path.Combine(failing, "cghm_metadata.json"), new ModMetadata { Id = "cgh.failure", Name = "Failure fixture", Version = "1.0.0", Dependencies = [new() { Id = "cgh.example" }] });
        await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "fault" && m.Text?.Contains("EXPECTED_START_FAILURE") == true), "failed mod startup", 90);
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "failed startup rollback", 30);
        Check(!messages.Any(m => m.Kind == "state" && m.Text == "running"), "failed Start never reports session running");
        Check(File.Exists(Path.Combine(distribution, "Data/cgh.failure/cleanup.txt")), "failed Start invokes cleanup");
        Check(messages.Any(m => m.Text == "Native module stopped"), "failed dependent Start rolls back native dependency");
        _ = Directory.CreateDirectory(Path.Combine(failing, "Resources"));
        File.WriteAllText(Path.Combine(failing, "Resources/fail-load"), "");
        messages.Clear(); await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "fault" && m.Text?.Contains("EXPECTED_LOAD_FAILURE") == true), "failed mod Load", 90);
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "failed Load rollback", 30);
        Check(!messages.Any(m => m.Kind == "state" && m.Text == "running"), "failed Load rejected and rolled back");
        settings.DisabledMods = ["cgh.failure"]; Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
        messages.Clear(); await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "starting"), "launch begins", 20);
        await client.SendAsync("stop");
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "cancel startup", 40);
        Check(!messages.Any(m => m.Kind == "fault"), "stopping during startup is a clean cancellation");

        // Exercise the SAME native and managed mod inside an MRT, not just a managed-only worker.
        var examplePath = Path.Combine(distribution, "Mods/BridgeDemo/cghm_metadata.json");
        var example = Json.Read<ModMetadata>(examplePath); Json.Write(examplePath, example with { LoadAsSubprocess = true });
        messages.Clear(); await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "running"), "dual-language isolated mod", 90, messages);
        await Until(() => messages.Any(m => m.Text?.Contains("round trip passed") == true), "isolated bridge round trip", 20, messages);
        Check(messages.Any(m => m.Text == "Native module loaded in isolated MRT"), "native module hosted in per-mod MRT");
        Check(messages.Any(m => m.Text == "Managed -> native direct service call passed"), "isolated C# and C++ retain direct bridge");
        await client.SendAsync("stop"); await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "isolated session stop", 40);
    }
    Console.WriteLine($"PASS {passed} checks; artifacts: {output}");
    File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS {passed} checks; realGame={real}; {DateTimeOffset.Now:O}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine("Evidence: " + output); return 1; }

static bool IsAlive(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
static async Task Until(Func<bool> condition, string name, int seconds, ConcurrentQueue<WireMessage>? messages = null)
{
    var watch = Stopwatch.StartNew();
    while (!condition())
    {
        if (messages?.FirstOrDefault(m => m.Kind is "fault" or "fatal") is { } fault) throw new Exception(name + ": " + fault.Text);
        if (watch.Elapsed.TotalSeconds > seconds) throw new TimeoutException(name);
        await Task.Delay(100);
    }
}
static void CopyTree(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    foreach (var dir in Directory.EnumerateDirectories(source))
        if (Path.GetFileName(dir) is not ("Sessions" or "Data")) CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
}
