using System.Text;
using System.Text.RegularExpressions;
using ConsGHost.Core;

namespace ConsGHost.Runtime;

internal static partial class GmaArchive
{
    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: true);

    [GeneratedRegex(@"^(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|(?:COM|LPT)[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDeviceName();

    public static async Task ExtractAsync(string source, string destination, CancellationToken ct)
    {
        const int bufferSize = 81920;
        await using var archive = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        using var reader = new BinaryReader(archive, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(4).AsSpan().SequenceEqual("GMAD"u8))
            throw new InvalidDataException("Expected an uncompressed GMAD archive.");
        int version = reader.ReadByte();
        if (version is < 1 or > 3) throw new InvalidDataException($"Unsupported GMAD format version {version}.");
        _ = reader.ReadUInt64(); // Steam ID
        _ = reader.ReadUInt64(); // Timestamp
        if (version > 1)
        {
            while (ReadField(reader).Length > 0) ct.ThrowIfCancellationRequested();
        }
        _ = ReadField(reader); // Title
        _ = ReadField(reader); // Description
        _ = ReadField(reader); // Author
        _ = reader.ReadInt32(); // Addon version

        List<(string Path, long Length)> files = [];
        long total = 0;
        while (reader.ReadUInt32() != 0)
        {
            ct.ThrowIfCancellationRequested();
            var path = EntryPath(destination, Utf8.GetString(ReadField(reader)));
            long length = reader.ReadInt64();
            _ = reader.ReadUInt32(); // File CRC
            if (length < 0 || length > archive.Length - total)
                throw new InvalidDataException("Invalid GMAD file length.");
            files.Add((path, length));
            total += length;
        }
        if (total > archive.Length - archive.Position)
            throw new InvalidDataException("Truncated GMAD payload.");

        byte[] buffer = new byte[bufferSize];
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            await using var output = new FileStream(file.Path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
            for (long remaining = file.Length; remaining > 0;)
            {
                int count = (int)Math.Min(remaining, buffer.Length);
                await archive.ReadExactlyAsync(buffer.AsMemory(0, count), ct);
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                remaining -= count;
            }
        }
    }

    private static byte[] ReadField(BinaryReader reader)
    {
        using var bytes = new MemoryStream();
        for (byte value = reader.ReadByte(); value != 0; value = reader.ReadByte())
        {
            if (bytes.Length >= 1024 * 1024) throw new InvalidDataException("GMAD string exceeds 1 MiB.");
            bytes.WriteByte(value);
        }
        return bytes.ToArray();
    }

    private static string EntryPath(string destination, string path)
    {
        var parts = path.Split(['/', '\\']);
        if (parts.Any(part => part.Length == 0 || part.TrimEnd(' ', '.') != part ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            WindowsDeviceName().IsMatch(part.Split('.')[0].TrimEnd())))
            throw new InvalidDataException($"Unsafe path in GMAD archive: '{path}'.");
        return ModCatalog.SafePath(destination, Path.Combine(parts));
    }
}
