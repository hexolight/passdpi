using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PassDpi.Core.Protocol;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PipeMessage))]
internal partial class ProtocolJsonContext : JsonSerializerContext { }

/// <summary>
/// Newline-delimited JSON framing with a hard size cap, so a bad client can't make
/// the service buffer unbounded data.
/// </summary>
public static class MessageFraming
{
    public const int MaxMessageBytes = 64 * 1024;

    public static string Serialize(PipeMessage m) =>
        JsonSerializer.Serialize(m, ProtocolJsonContext.Default.PipeMessage);

    public static PipeMessage? Deserialize(string line)
    {
        try { return JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.PipeMessage); }
        catch (JsonException) { return null; }
    }

    public static async Task WriteAsync(Stream s, PipeMessage m, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(Serialize(m) + "\n");
        if (bytes.Length > MaxMessageBytes) throw new InvalidOperationException("Message too large");
        await s.WriteAsync(bytes, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads one line. Returns null on EOF. Throws if the line exceeds the cap.</summary>
    public static async Task<string?> ReadLineAsync(Stream s, MemoryStream buffer, byte[] chunk, CancellationToken ct)
    {
        while (true)
        {
            // Look for a newline already buffered.
            var data = buffer.GetBuffer();
            var len = (int)buffer.Length;
            var nl = Array.IndexOf(data, (byte)'\n', 0, len);
            if (nl >= 0)
            {
                var line = Encoding.UTF8.GetString(data, 0, nl);
                var rest = len - nl - 1;
                Buffer.BlockCopy(data, nl + 1, data, 0, rest);
                buffer.SetLength(rest);
                return line;
            }
            if (len > MaxMessageBytes) throw new InvalidDataException("Message too large");

            var n = await s.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (n == 0) return null;
            buffer.Position = buffer.Length;
            buffer.Write(chunk, 0, n);
        }
    }
}
