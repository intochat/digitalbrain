using System.Buffers.Binary;
using System.Text.Json;
using DigitalBrain.AI.PersonaPlex;

namespace DigitalBrain.Kernel;

internal enum PersonaPlexVoiceControl
{
    Start,
    Stop,
}

internal sealed class PersonaPlexVoiceProtocolException : Exception
{
    public PersonaPlexVoiceProtocolException(string message)
        : base(message)
    {
    }

    public PersonaPlexVoiceProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class PersonaPlexVoiceProtocol
{
    internal const int Version = 1;
    internal const int HeaderByteCount = 16;
    internal const int SampleCount = 1920;
    internal const int PcmByteCount = 3840;
    internal const int PacketByteCount = HeaderByteCount + PcmByteCount;

    public static byte[] EncodeAudio(PersonaPlexAudioFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var packet = new byte[PacketByteCount];
        BinaryPrimitives.WriteInt32LittleEndian(packet, Version);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(sizeof(int)), frame.Sequence);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(sizeof(int) + sizeof(long)), SampleCount);

        var payload = packet.AsSpan(HeaderByteCount);
        var pcm = frame.Pcm16.Span;
        for (var sample = 0; sample < pcm.Length; sample++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload[(sample * sizeof(short))..], pcm[sample]);
        }

        return packet;
    }

    public static PersonaPlexAudioFrame DecodeAudio(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != PacketByteCount)
        {
            throw new InvalidDataException("PersonaPlex audio packets require exactly 3,840 PCM payload bytes.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(packet);
        if (version != Version)
        {
            throw new InvalidDataException("Unsupported PersonaPlex voice protocol version.");
        }

        var sequence = BinaryPrimitives.ReadInt64LittleEndian(packet[sizeof(int)..]);
        if (sequence <= 0)
        {
            throw new InvalidDataException("PersonaPlex audio sequence numbers must be positive.");
        }

        var sampleCount = BinaryPrimitives.ReadInt32LittleEndian(packet[(sizeof(int) + sizeof(long))..]);
        if (sampleCount != SampleCount)
        {
            throw new InvalidDataException("PersonaPlex audio packets require exactly 1,920 samples.");
        }

        var pcm = new short[SampleCount];
        var payload = packet[HeaderByteCount..];
        for (var sample = 0; sample < pcm.Length; sample++)
        {
            pcm[sample] = BinaryPrimitives.ReadInt16LittleEndian(payload[(sample * sizeof(short))..]);
        }

        return PersonaPlexAudioFrame.Create(sequence, pcm);
    }

    public static PersonaPlexVoiceControl DecodeControl(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("PersonaPlex control messages require a type.");
            }

            return type.GetString() switch
            {
                "start" => PersonaPlexVoiceControl.Start,
                "stop" => PersonaPlexVoiceControl.Stop,
                _ => throw new InvalidDataException("Unsupported PersonaPlex control message type."),
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("PersonaPlex control message is not valid JSON.", exception);
        }
    }

    public static byte[] EncodeStatus(string state, string message)
        => JsonSerializer.SerializeToUtf8Bytes(new { type = "status", state, message });

    public static byte[] EncodeError(string code, string message)
        => JsonSerializer.SerializeToUtf8Bytes(new { type = "error", code, message });

    public static byte[] EncodeStop()
        => "{\"type\":\"stop\"}"u8.ToArray();
}
