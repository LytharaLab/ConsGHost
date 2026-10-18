using System.Buffers.Binary;
using System.Text.Json;

namespace ConsGHost.Core;

public sealed record WireMessage
{
    public required string Kind { get; init; }
    public string? Token { get; init; }
    public string? Endpoint { get; init; }
    public string? Text { get; init; }
    public string? Level { get; init; }
    public string? Mod { get; init; }
    public string? Topic { get; init; }
    public byte[]? Payload { get; init; }
    public int ProcessId { get; init; }
}

public sealed class Wire(Stream stream) : IAsyncDisposable
{
    public const int MaxPacket = 1024 * 1024;
    private readonly SemaphoreSlim _writer = new(1);
    public async Task SendAsync(WireMessage message, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json.Options);
        if (bytes.Length > MaxPacket) throw new InvalidDataException("IPC packet exceeds 1 MiB.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await _writer.WaitAsync(ct);
        try { await stream.WriteAsync(header, ct); await stream.WriteAsync(bytes, ct); await stream.FlushAsync(ct); }
        finally { _writer.Release(); }
    }
    public async Task<WireMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), ct);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > MaxPacket) throw new InvalidDataException("Invalid IPC packet length.");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<WireMessage>(bytes, Json.Options) ?? throw new InvalidDataException("Null IPC packet.");
    }
    public async ValueTask DisposeAsync() { await stream.DisposeAsync(); }
}
