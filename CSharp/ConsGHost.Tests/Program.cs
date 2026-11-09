using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ConsGHost.Core;
using ConsGHost.SDK;

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
    Json.Write(Path.Combine(invalid, "cghm_metadata.json"), new ModMetadata
    {
        Id = "bad", Name = "bad", Version = "1.0.0", IgnoredAddons = ["../outside"]
    });
    Check(ModCatalog.Scan(Path.GetDirectoryName(invalid)!).Issues.Single().Message.Contains("ignoredAddons"), "addon ignore entries reject paths");
    var configurable = Path.Combine(output, "config-catalog", "Configured");
    Directory.CreateDirectory(configurable);
    Json.Write(Path.Combine(configurable, "cghm_metadata.json"), new ModMetadata { Id = "configured", Name = "Configured", Version = "1.0.0" });
    var configMods = Path.GetDirectoryName(configurable)!;
    var template = Path.Combine(configurable, ModConfig.DefaultFileName);
    File.WriteAllText(template, """{"flag":true,"nested":{"count":2},"optional":null}""", Encoding.UTF8);
    Check(ModCatalog.Scan(configMods).Mods.Single().HasConfig, "mod scan discovers a configuration template");
    var config = ModConfig.Open(configurable)!;
    Check(File.ReadAllBytes(config.FilePath).SequenceEqual(File.ReadAllBytes(template)), "first scan copies default configuration byte for byte");
    const string edited = """{"flag":false,"nested":{"custom":"中文"},"unknown":[1,2]}""";
    config.SaveRaw(edited, config.ReadRaw());
    Check(ModCatalog.Scan(configMods).Mods.Single().HasConfig && config.ReadRaw() == edited, "rescan preserves edited config file");
    IModConfig api = config;
    var snapshot = api.Read();
    Check(!api.Get<bool>("flag") && snapshot.GetProperty("nested").GetProperty("count").GetInt32() == 2 &&
        snapshot.GetProperty("unknown").GetArrayLength() == 2, "SDK reads merged defaults and preserves unknown values");
    var original = config.ReadRaw();
    await Task.WhenAll(Task.Run(() => api.Set("optional", "set")), Task.Run(() => api.Set("external", 42)));
    Check(api.Get<string>("optional") == "set" && api.Get<int>("external") == 42, "concurrent config key updates both persist");
    try { config.SaveRaw(edited, original); throw new InvalidOperationException("Stale configuration overwritten"); }
    catch (IOException) { Check(api.Get<int>("external") == 42, "reject stale editor saves"); }
    var current = config.ReadRaw();
    Reject(() => config.SaveRaw("""{"flag":"yes"}""", current), "reject wrong config value types");
    Reject(() => config.SaveRaw("""{"flag":true,"flag":false}""", current), "reject duplicate config keys");
    File.WriteAllText(config.FilePath, "{ invalid");
    Check(ModCatalog.Scan(configMods).Mods.Single().HasConfig, "malformed user config remains accessible for repair");
    config.SaveRaw(edited, config.ReadRaw());
    Check(config.ReadRaw() == edited && ModConfig.Open(output) == null, "repair user config and allow mods without templates");
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
    CghVersion.VerifyDistribution(distribution);
    Check(CghVersion.Current == File.ReadAllText(Path.Combine(repo, "VERSION")).Trim(), "distribution matches source CGH version");
    var nativeRuntime = Path.Combine(distribution, "cgh_runtime_win64.dll");
    File.Move(nativeRuntime, nativeRuntime + ".original");
    try
    {
        File.Copy(Path.Combine(repo, "Cpp/build/Release/cgh_test_extra.dll"), nativeRuntime);
        Reject(() => CghVersion.VerifyDistribution(distribution), "unversioned native component rejected");
    }
    finally { File.Move(nativeRuntime + ".original", nativeRuntime, overwrite: true); }
    File.Copy(Path.Combine(repo, "Cpp/build/Release/server.dll"), Path.Combine(game, "server.dll"));
    File.Copy(Path.Combine(repo, "Cpp/build/Release/cgh_test_extra.dll"), Path.Combine(extra, "cgh_test_extra.dll"));
    var settings = new HostSettings { GameDirectory = game, GameArguments = ["45"], ExtraLibraries = [new() { Path = extra }] };
    if (real) { settings.GameDirectory = @"L:\SteamLibrary\steamapps\common\GarrysMod"; settings.GameArguments = ["-console", "-novid", "-insecure", "-noaddons", "-noworkshop", "-windowed", "-w", "1024", "-h", "768", "+map", "gm_construct"]; settings.StartupTimeoutSeconds = 120; }
    var gameAddons = Path.Combine(game, "garrysmod", "addons");
    var stageRoot = Path.Combine(game, "garrysmod", ".cgh-addons");
    var addonSource = Path.Combine(distribution, "Mods", "AddonFixture", "Addons");
    var existingAddon = Path.Combine(gameAddons, "cgh_" + Guid.NewGuid().ToString("N"));
    var bridgeConfig = ModConfig.Open(Path.Combine(distribution, "Mods", "BridgeDemo"))!;
    bridgeConfig.Set("benchmarkIterations", 100001);
    var nativeConfigMod = Path.Combine(distribution, "Mods", "ConfigFixture");
    Directory.CreateDirectory(Path.Combine(nativeConfigMod, "Native"));
    File.Copy(Path.Combine(extra, "cgh_test_extra.dll"), Path.Combine(nativeConfigMod, "Native", "cgh_config_fixture.dll"));
    Json.Write(Path.Combine(nativeConfigMod, "cghm_metadata.json"), new ModMetadata
    {
        Id = "fixture.config", Name = "Native config fixture", Version = "1.0.0"
    });
    File.WriteAllText(Path.Combine(nativeConfigMod, ModConfig.DefaultFileName), """{"nativeProbe":41}""");
    if (!real)
    {
        // Exercise an unrelated installed addon and owned leftovers from an interrupted Runtime.
        Directory.CreateDirectory(existingAddon);
        File.WriteAllText(Path.Combine(existingAddon, "preserve.txt"), "pre-existing addon");
        Directory.CreateDirectory(stageRoot);
        foreach (var parent in new[] { gameAddons, stageRoot })
        {
            var name = "cgh_" + Guid.NewGuid().ToString("N");
            var stale = Path.Combine(parent, name);
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stageRoot, name + ".owner"), name);
        }
        Directory.CreateDirectory(addonSource);
        Json.Write(Path.Combine(Path.GetDirectoryName(addonSource)!, "cghm_metadata.json"), new ModMetadata
        {
            Id = "fixture.addons", Name = "Addon fixtures", Version = "1.0.0", IgnoredAddons = ["IGNORE-ME", "SKIP.GMA", "not-installed"]
        });
        foreach (var name in new[] { "folder-one", "folder-two", "ignore-me" })
        {
            var autorun = Path.Combine(addonSource, name, "lua", "autorun");
            Directory.CreateDirectory(autorun);
            File.WriteAllText(Path.Combine(autorun, name + ".lua"), "-- fixture");
        }
        WriteGma(Path.Combine(addonSource, "archive.GMA"), "lua/autorun/packed.lua", "-- archive fixture");
        File.WriteAllText(Path.Combine(addonSource, "skip.gma"), "ignored invalid archive");
        File.WriteAllText(Path.Combine(addonSource, "readme.txt"), "not an addon");
        var disabled = Path.Combine(distribution, "Mods", "DisabledAddon");
        Directory.CreateDirectory(Path.Combine(disabled, "Addons", "disabled-addon"));
        Json.Write(Path.Combine(disabled, "cghm_metadata.json"), new ModMetadata { Id = "fixture.disabled", Name = "Disabled addon", Version = "1.0.0" });
        settings.DisabledMods.Add("fixture.disabled");
        var discovered = ModCatalog.Scan(Path.Combine(distribution, "Mods")).Mods.Single(mod => mod.Metadata.Id == "fixture.addons").Addons;
        Check(discovered.Count == 5 && discovered.Count(addon => addon.Ignored) == 2, "scan standard folders and GMA, match ignore names case-insensitively");
    }
    Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
    var messages = new ConcurrentQueue<WireMessage>();
    await using var client = new RuntimeClient(distribution);
    client.Message += m => { messages.Enqueue(m); if (m.Kind != "heartbeat") Console.WriteLine($"  {m.Kind} {m.Endpoint} {m.Mod}: {m.Text}"); };
    string? previousHeap = Environment.GetEnvironmentVariable("_no_debug_heap");
    string? previousInherited = Environment.GetEnvironmentVariable("CGH_TEST_INHERITED");
    try
    {
        if (!real)
        {
            Environment.SetEnvironmentVariable("_no_debug_heap", "0");
            Environment.SetEnvironmentVariable("CGH_TEST_INHERITED", "path中");
        }
        await client.ConnectAsync();
    }
    finally
    {
        if (!real)
        {
            Environment.SetEnvironmentVariable("_no_debug_heap", previousHeap);
            Environment.SetEnvironmentVariable("CGH_TEST_INHERITED", previousInherited);
        }
    }
    await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "runtime handshake", 20);
    await client.SendAsync("launch");
    await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "running"), "controlled process launch", 150, messages);
    Check(true, real ? "real GMod bootstrapped and mod startup acknowledged" : "fixture game bootstrapped before entry and mod startup acknowledged");
    Check(messages.Any(m => m.Kind == "log" && m.Text == $"CGH version: {CghVersion.Current}"), "session records CGH version");
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
    if (!real)
    {
        Check(messages.Any(m => m.Text == "CGH_EXTRA_PROBE=42"), "game can invoke extra dependency export");
        Check(messages.Any(m => m.Text == "CGH game environment verified"), "game overrides debug heap and inherits Unicode environment");
        var staged = Directory.GetDirectories(gameAddons).Where(path => path != existingAddon).ToArray();
        Check(staged.Length == 3 && !Directory.EnumerateDirectories(stageRoot).Any(), "stage only selected addons and recover stale copies");
        Check(staged.SelectMany(path => Directory.GetFiles(path, "*.lua", SearchOption.AllDirectories)).Select(Path.GetFileName)
            .Order().SequenceEqual(["folder-one.lua", "folder-two.lua", "packed.lua"]), "stage multiple folder addons and expand GMA using its directory structure");
        Check(File.ReadAllText(staged.SelectMany(path => Directory.GetFiles(path, "packed.lua", SearchOption.AllDirectories)).Single())
            == "-- archive fixture", "extract GMA file payload intact");
        await using var competing = new RuntimeClient(distribution);
        var reservationFault = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        competing.Message += m => { if (m.Kind == "fault") reservationFault.TrySetResult(m.Text ?? ""); };
        await competing.ConnectAsync();
        await competing.SendAsync("launch");
        Check((await reservationFault.Task.WaitAsync(TimeSpan.FromSeconds(15))).Contains("reserve GMod addon staging") &&
            staged.All(Directory.Exists), "a concurrent CGH session cannot remove the active addon's copies");
    }
    var metrics = Path.Combine(distribution, "Data/cgh.example/bridge-metrics.json");
    using (var benchmark = JsonDocument.Parse(File.ReadAllText(metrics)))
        Check(benchmark.RootElement.GetProperty("iterations").GetInt32() == 100001, "managed mod reads its edited benchmark config through the SDK");
    Check(ModConfig.Open(nativeConfigMod)!.Read().GetProperty("nativeProbe").GetInt32() == 42, "native mod reads and updates config through the ABI extension");
    settings.DisabledMods.Add("fixture.config");
    Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
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
        Check(Directory.GetDirectories(gameAddons).SequenceEqual([existingAddon]) && !Directory.EnumerateFileSystemEntries(stageRoot).Any() &&
            File.Exists(Path.Combine(existingAddon, "preserve.txt")), "stop removes only CGH addon copies");

        // An error after several successful copies rolls them all back and releases the game lease.
        var lockedDirectory = Path.Combine(addonSource, "zz-locked");
        Directory.CreateDirectory(lockedDirectory);
        using (File.Create(Path.Combine(lockedDirectory, "locked.dat")))
        {
            messages.Clear();
            await client.SendAsync("launch");
            await Until(() => messages.Any(m => m.Kind == "fault" && m.Text?.Contains("zz-locked") == true), "addon staging failure", 20);
            await Until(() => messages.Count(m => m.Kind == "state" && m.Text == "idle") >= 2, "failed addon stage cleanup", 20);
            Check(Directory.GetDirectories(gameAddons).SequenceEqual([existingAddon]) && !Directory.EnumerateFileSystemEntries(stageRoot).Any(),
                "addon copy failure rolls back partial stage");
        }
        Directory.Delete(lockedDirectory, recursive: true);

        var maliciousGma = Path.Combine(addonSource, "zz-escape.gma");
        WriteGma(maliciousGma, "../escape.lua", "-- should not be written");
        messages.Clear();
        await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "fault" && m.Text?.Contains("Unsafe path in GMAD") == true), "GMA path traversal", 20);
        await Until(() => messages.Count(m => m.Kind == "state" && m.Text == "idle") >= 2, "rejected GMA cleanup", 20);
        Check(Directory.GetDirectories(gameAddons).SequenceEqual([existingAddon]) && !Directory.EnumerateFileSystemEntries(stageRoot).Any(),
            "reject escaping GMA paths and roll back staging");
        File.Delete(maliciousGma);

        settings.GameArguments = ["45", "0", "-NOADDONS"];
        Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
        messages.Clear();
        await client.SendAsync("launch");
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "running"), "launch with -noaddons", 20, messages);
        Check(Directory.GetDirectories(gameAddons).SequenceEqual([existingAddon]) &&
            messages.Any(m => m.Text?.Contains("addon loading skipped") == true), "respect the -noaddons game argument");
        await client.SendAsync("stop");
        await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "stop with -noaddons", 20);
        settings.GameArguments = ["45"];
        Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);

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
        settings.DisabledMods.Add("cgh.failure"); Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
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

        settings.DisabledMods.AddRange(["cgh.example", "cgh.observer"]);
        foreach (int exitCode in new[] { 0, 23 })
        {
            settings.GameArguments = ["2", exitCode.ToString()];
            Json.Write(Path.Combine(distribution, "cgh_settings.json"), settings);
            messages.Clear(); await client.SendAsync("launch");
            await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "running"), "natural game launch", 20, messages);
            await Until(() => messages.Any(m => m.Kind == "state" && m.Text == "idle"), "natural game exit", 15);
            var faults = messages.Where(m => m.Kind is "fault" or "fatal").ToArray();
            Check(messages.Any(m => m.Kind == "log" && m.Text == $"Game exited: {exitCode}"), $"natural exit reports game code {exitCode}");
            Check(exitCode == 0 ? faults.Length == 0 : faults.Length == 1 && faults[0].Text == $"Game exited: {exitCode}",
                $"natural exit classifies game code {exitCode}");
            Check(!messages.Any(m => m.Level == "warning" && m.Text?.StartsWith("Stop:") == true), "no stop command to an exited game");
            Check(Directory.GetDirectories(gameAddons).SequenceEqual([existingAddon]), "natural exit removes staged GMod addons");
        }
    }
    Console.WriteLine($"PASS {passed} checks; artifacts: {output}");
    File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS {passed} checks; realGame={real}; {DateTimeOffset.Now:O}");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); Console.Error.WriteLine("Evidence: " + output); return 1; }

static bool IsAlive(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
static void WriteGma(string archive, string entry, string content)
{
    using var writer = new BinaryWriter(File.Create(archive));
    writer.Write("GMAD"u8);
    writer.Write((byte)3);
    writer.Write(0UL); // Steam ID
    writer.Write(0UL); // Timestamp
    writer.Write((byte)0); // Required content terminator
    writer.Write("CGH fixture\0{}\0CGH\0"u8);
    writer.Write(1); // Addon version
    writer.Write(1U); // File index
    writer.Write(Encoding.UTF8.GetBytes(entry));
    writer.Write((byte)0);
    byte[] data = Encoding.UTF8.GetBytes(content);
    writer.Write((long)data.Length);
    writer.Write(0U); // File CRC
    writer.Write(0U); // Index terminator
    writer.Write(data);
    writer.Write(0U); // Archive CRC
}
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
