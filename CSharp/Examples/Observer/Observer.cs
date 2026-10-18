using System.Text;
using ConsGHost.SDK;

namespace ConsGHost.Examples;

public sealed class Observer : ICghMod
{
    private IModContext _context = null!;
    public void Load(IModContext context)
    {
        _context = context;
        if (!context.IsIsolated) throw new InvalidOperationException("Observer must run in its own MRT process.");
        context.Subscribe("example.ping", (source, data) =>
        {
            context.Log("Isolated observer received game message from " + source);
            context.PublishAsync("observer.ack", data).GetAwaiter().GetResult();
        });
    }
    public void Start() => _context.PublishAsync("observer.ready", Encoding.UTF8.GetBytes($"MRT PID {Environment.ProcessId}")).GetAwaiter().GetResult();
    public void Stop() => _context.Log("Isolated observer stopped");
}
