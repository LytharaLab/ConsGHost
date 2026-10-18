using ConsGHost.SDK;

namespace ConsGHost.TestMod;

public sealed class FailureMod : ICghMod
{
    private IModContext _context = null!;
    public void Load(IModContext context)
    {
        _context = context;
        if (File.Exists(Path.Combine(context.ResourcesDirectory, "fail-load"))) throw new InvalidOperationException("EXPECTED_LOAD_FAILURE");
    }
    public void Start() => throw new InvalidOperationException("EXPECTED_START_FAILURE");
    public void Stop()
    {
        File.WriteAllText(Path.Combine(_context.DataDirectory, "cleanup.txt"), "STOP completed");
        _context.Log("Expected failing mod cleanup");
    }
}
