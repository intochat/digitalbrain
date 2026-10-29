using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>A journaled signal or state payload could not be encoded or decoded (unknown alias, malformed or too deep JSON).</summary>
public sealed class JournalPayloadException : IOException
{
    /// <summary>Creates the exception with its message.</summary>
    public JournalPayloadException(string message) : base(message) { }

    /// <summary>Creates the exception with its message and cause.</summary>
    public JournalPayloadException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates the exception.</summary>
    public JournalPayloadException() { }
}

/// <summary>
/// Signal and grain-state payloads as JSON through <see cref="RuntimeProtocol.Json"/> (06 D-14): module records need no Orleans
/// attributes. Signals carry their frozen <c>[Signal]</c> alias and decode only through the closed <see cref="SignalTypeMap"/>.
/// </summary>
internal sealed class JournalPayloads(SignalTypeMap types)
{
    public SignalTypeMap Types { get; } = types;

    public static byte[] EncodeState<T>(T value)
    {
        try { return JsonSerializer.SerializeToUtf8Bytes(value, RuntimeProtocol.Json); }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { throw new JournalPayloadException($"State {typeof(T).FullName} cannot be written as JSON.", error); }
    }

    public static T? DecodeState<T>(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try { return JsonSerializer.Deserialize<T>(json, RuntimeProtocol.Json); }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { throw new JournalPayloadException($"Stored state is not a valid {typeof(T).FullName}.", error); }
    }

    public JournaledSignalPayload EncodeSignal(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var type = signal.GetType();
        if (!Types.TryAliasOf(type, out var alias))
        { throw new JournalPayloadException($"Signal {type.FullName} has no registered [Signal] alias; register its assembly with AddJournalPayloads."); }
        try { return new JournaledSignalPayload(alias, JsonSerializer.SerializeToUtf8Bytes(signal, type, RuntimeProtocol.Json)); }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { throw new JournalPayloadException($"Signal {alias} cannot be written as JSON.", error); }
    }

    public Signal DecodeSignal(JournaledSignalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!Types.TryResolve(payload.Type, out var type))
        { throw new JournalPayloadException($"Journaled signal '{payload.Type}' is not a registered [Signal] alias."); }
        try
        {
            return JsonSerializer.Deserialize(payload.Json, type, RuntimeProtocol.Json) as Signal
                ?? throw new JournalPayloadException($"Journaled signal '{payload.Type}' is null.");
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        { throw new JournalPayloadException($"Journaled signal '{payload.Type}' is not valid JSON for {type.FullName}.", error); }
    }
}
