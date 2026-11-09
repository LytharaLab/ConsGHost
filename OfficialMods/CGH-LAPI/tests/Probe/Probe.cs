using System.Text;
using System.Text.Json;
using ConsGHost.SDK;

public sealed class Probe : ICghMod
{
    private const string Bridge = "cgh.lapi";
    private const string Payload = "bridge-你好\0bytes";
    private const string Request = "cgh.lapi.test.request";
    private IModContext _context = null!;

    public void Load(IModContext context)
    {
        _context = context;
        if (context.IsIsolated)
        {
            context.Subscribe(Request, HandleIsolatedRequest);
            context.Subscribe("cgh.lapi.version.reply." + context.Id, HandleIsolatedVersion);
            return;
        }

        context.RegisterService("echo", (input, output) =>
        {
            if (input.Length > output.Length) return -3;
            input.CopyTo(output);
            return input.Length;
        });
        context.RegisterService("version", (input, output) =>
            ValidRealm(input, out var realm) ? context.Call(Bridge, $"version.{realm}", [], output) : -6);
        context.RegisterService("queue", QueueEvents);
        context.RegisterService("passed", (input, _) =>
        {
            if (!ValidRealm(input, out var realm)) return -6;
            context.Log("LAPI_TEST_PASS " + realm);
            return 0;
        });
        context.RegisterService("failed", (input, _) =>
        {
            context.Log("LAPI_TEST_FAIL " + Encoding.UTF8.GetString(input), "error");
            return 0;
        });
    }

    private int QueueEvents(ReadOnlySpan<byte> input, Span<byte> _)
    {
        if (!ValidRealm(input, out var realm)) return -6;
        var sent = _context.Call(Bridge, $"{realm}.emit", Encoding.UTF8.GetBytes("direct\0" + Payload), []);
        return sent < 0 ? sent :
            _context.Call(Bridge, $"{realm}.hook", Encoding.UTF8.GetBytes("CGHLAPITestForce\0" + Payload), []);
    }

    private void HandleIsolatedRequest(string source, ReadOnlyMemory<byte> data)
    {
        if (source != Bridge || !ValidRealm(data.Span, out var realm)) return;
        _context.PublishAsync($"cgh.lapi.version.{realm}", data).GetAwaiter().GetResult();
    }

    private void HandleIsolatedVersion(string source, ReadOnlyMemory<byte> data)
    {
        if (source != Bridge) return;
        var separator = data.Span.IndexOf((byte)0);
        if (separator < 0 || !ValidRealm(data.Span[..separator], out var realm))
        {
            _context.Log("LAPI_TEST_FAIL invalid isolated version frame", "error");
            return;
        }
        using var version = JsonDocument.Parse(data[(separator + 1)..]);
        if (!version.RootElement.TryGetProperty("realm", out var target) || target.GetString() != realm ||
            !version.RootElement.TryGetProperty("version", out var number) || number.GetInt32() <= 0)
        {
            _context.Log("LAPI_TEST_FAIL invalid isolated version JSON", "error");
            return;
        }
        _context.PublishAsync($"cgh.lapi.{realm}.emit", Encoding.UTF8.GetBytes("isolated\0" + Payload))
            .GetAwaiter().GetResult();
        _context.PublishAsync($"cgh.lapi.{realm}.hook", Encoding.UTF8.GetBytes("CGHLAPITestIsolated\0" + Payload))
            .GetAwaiter().GetResult();
    }

    private static bool ValidRealm(ReadOnlySpan<byte> data, out string realm)
    {
        realm = Encoding.UTF8.GetString(data);
        return realm is "server" or "client";
    }
}
