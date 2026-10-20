using System.Buffers.Binary;
using System.Text.Json;

namespace DropWheel.Ipc;

/// <summary>
/// Формат сообщения: 4 байта длины (little-endian) + JSON-массив строк в UTF-8.
/// В ответ сервер шлёт один байт Ack — клиент знает, что пути дошли.
/// </summary>
internal static class PipeProtocol
{
    public const byte Ack = 0x06;
    /// <summary>Защита от мусора: 1000 путей по 260 символов укладываются с большим запасом.</summary>
    public const int MaxMessageBytes = 16 * 1024 * 1024;

    public static byte[] Encode(string[] args)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(args);
        var message = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message, json.Length);
        json.CopyTo(message, 4);
        return message;
    }

    /// <summary>null — сообщение битое.</summary>
    public static async Task<string[]?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaxMessageBytes) return null;

        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        try
        {
            return JsonSerializer.Deserialize<string?[]>(body)?.OfType<string>().ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
