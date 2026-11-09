using ConsGHost.Core;

namespace ConsGHost.Runtime;

internal sealed class AddonStage(string gameRoot, FileStream lease) : IDisposable
{
    private const string Prefix = "cgh_";
    private const string ReceiptExtension = ".owner";
    private readonly string _workspace = Path.Combine(gameRoot, ".cgh-addons");
    private readonly string _addons = Path.Combine(gameRoot, "addons");

    public static async Task<AddonStage?> PrepareAsync(string gameDirectory, IReadOnlyList<ModAddon> addons, CancellationToken ct)
    {
        var gameRoot = Path.Combine(gameDirectory, "garrysmod");
        if (!Directory.Exists(gameRoot)) return null;

        FileStream lease;
        try
        {
            lease = new(Path.Combine(gameRoot, ".cgh-addons.lock"), FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException error)
        {
            throw new IOException("Could not reserve GMod addon staging. Check whether another CGH session is using this installation.", error);
        }

        var stage = new AddonStage(gameRoot, lease);
        try
        {
            // A previous Runtime may have been terminated before it could remove its copies.
            stage.RemoveCopies();
            if (addons.Count > 0)
            {
                Directory.CreateDirectory(stage._workspace);
                Directory.CreateDirectory(stage._addons);
                RequireOrdinaryPath(stage._workspace);
                RequireOrdinaryPath(stage._addons);
                for (int index = 0; index < addons.Count; index++)
                    await stage.CopyAddonAsync(addons[index], index, ct);
            }
            return stage;
        }
        catch (Exception error)
        {
            try { stage.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Could not stage GMod addons or clean up the partial copies.", error, cleanup); }
            throw;
        }
    }

    private async Task CopyAddonAsync(ModAddon addon, int index, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Keep filesystem order stable across sessions while retaining a unique staging name.
        var nonce = Guid.NewGuid().ToString("N");
        var name = $"{Prefix}{index:x8}{nonce[8..]}";
        var work = Path.Combine(_workspace, name);
        var destination = Path.Combine(_addons, name);
        try
        {
            if (Path.Exists(work) || Path.Exists(destination)) throw new IOException("CGH addon staging name is already in use.");
            // Keep the receipt outside the copy so it survives an interrupted recursive delete.
            using (var receipt = new StreamWriter(new FileStream(work + ReceiptExtension, FileMode.CreateNew)))
                receipt.Write(name);
            Directory.CreateDirectory(work);
            if (addon.IsArchive)
            {
                // Use the same filesystem loader for both formats, including launches with -noworkshop.
                RequireOrdinaryPath(addon.Path);
                await GmaArchive.ExtractAsync(addon.Path, work, ct);
            }
            else
                await CopyDirectoryAsync(addon.Path, work, ct);
            await PublishAsync(work, destination, ct);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            throw new IOException($"Could not stage GMod addon '{addon.Name}' from '{addon.Path}': {error.Message}", error);
        }
    }

    private static async Task PublishAsync(string work, string destination, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { Directory.Move(work, destination); return; }
            catch (Exception error) when (attempt < 5 && error is (IOException or UnauthorizedAccessException))
            {
                // A scanner may briefly open a copied file before Windows can rename its directory.
                await Task.Delay(100, ct);
            }
        }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken ct)
    {
        RequireOrdinaryPath(source);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((RequireOrdinaryPath(entry) & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                await CopyDirectoryAsync(entry, target, ct);
            }
            else
                await CopyFileAsync(entry, target, ct);
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        const int bufferSize = 81920;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await input.CopyToAsync(output, ct);
    }

    private void RemoveCopies()
    {
        if (!Directory.Exists(_workspace)) return;
        RequireOrdinaryPath(_workspace);
        if (Directory.Exists(_addons)) RequireOrdinaryPath(_addons);
        List<Exception> failures = [];
        foreach (var receipt in new DirectoryInfo(_workspace).EnumerateFiles(Prefix + "*" + ReceiptExtension))
        {
            var name = Path.GetFileNameWithoutExtension(receipt.Name);
            if (!name.StartsWith(Prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out _) ||
                receipt.Length != name.Length || (receipt.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            try
            {
                if (File.ReadAllText(receipt.FullName) != name) continue;
                DeleteCopy(Path.Combine(_addons, name));
                DeleteCopy(Path.Combine(_workspace, name));
                receipt.Delete();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failures.Add(error); }
        }
        if (failures.Count > 0) throw new AggregateException("Could not remove all staged GMod addons.", failures);
    }

    private static void DeleteCopy(string path)
    {
        if (!Directory.Exists(path)) return;
        RequireOrdinaryPath(path);
        Directory.Delete(path, recursive: true);
    }

    private static FileAttributes RequireOrdinaryPath(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("GMod addon staging cannot traverse a symbolic link or junction: " + path);
        return attributes;
    }

    public void Dispose()
    {
        try { RemoveCopies(); }
        finally { lease.Dispose(); }
    }
}
