using System.Buffers;
using System.Text.Json;

namespace DigitalBrain.Invocation;

/// <summary>
/// Encodes a value once into a per-thread reused buffer and writer, then keeps the bytes as a self-owned <see cref="JsonElement"/>:
/// one encoding pass, no per-call writer or growth buffers.
/// </summary>
internal static class PooledJson
{
    [ThreadStatic]
    private static ArrayBufferWriter<byte>? _buffer;

    [ThreadStatic]
    private static Utf8JsonWriter? _writer;

    // Buffers above this size are dropped after use instead of being kept per thread.
    private const int KeepBytes = 256 * 1024;

    public static JsonElement Encode(object? value, Type type)
    {
        var buffer = _buffer ??= new ArrayBufferWriter<byte>(256);
        var writer = _writer ??= new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = RuntimeProtocol.Json.Encoder, Indented = false, MaxDepth = RuntimeProtocol.Json.MaxDepth, SkipValidation = true });
        try
        {
            JsonSerializer.Serialize(writer, value, type, RuntimeProtocol.Json);
            writer.Flush();
            return JsonElement.Parse(buffer.WrittenSpan, new JsonDocumentOptions { MaxDepth = RuntimeProtocol.Json.MaxDepth });
        }
        finally
        {
            writer.Reset();
            if (buffer.Capacity > KeepBytes) { _buffer = null; _writer = null; }
            else { buffer.ResetWrittenCount(); }
        }
    }
}
