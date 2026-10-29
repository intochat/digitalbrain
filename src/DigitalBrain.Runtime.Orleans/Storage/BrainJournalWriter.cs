using System.Text.Json;
using DigitalBrain.Runtime.Orleans.Synapses;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>What an entry written through <see cref="IBrainJournalWriter"/> does to its state record.</summary>
public enum JournalStateChange
{
    /// <summary>The entry carries signals only.</summary>
    None = 0,

    /// <summary>The entry writes the record.</summary>
    Write = 1,

    /// <summary>The entry removes the record.</summary>
    Clear = 2,
}

/// <summary>
/// The state part of an adapter entry: an opaque record (for example an Orleans-serialized legacy grain state), never decoded by
/// the runtime.
/// </summary>
/// <param name="GrainId">The grain the record belongs to.</param>
/// <param name="StateName">The state name within the grain.</param>
/// <param name="Change">What the entry does to the record.</param>
/// <param name="ExpectedETag">The ETag the record must still have (null when it must not exist); checked unless <paramref name="Change"/> is None.</param>
/// <param name="Record">The opaque record bytes (ignored unless <paramref name="Change"/> is Write).</param>
/// <param name="StateType">The state type name stored beside the record, or null.</param>
public sealed record JournalStateWrite(string GrainId, string StateName, JournalStateChange Change, long? ExpectedETag,
    ReadOnlyMemory<byte> Record, string? StateType);

/// <summary>One signal of an adapter entry: its type name and its JSON payload.</summary>
/// <param name="Type">The signal type name; a DigitalBrain <c>[Signal]</c> alias when plain subscribers should receive it.</param>
/// <param name="Json">The UTF-8 JSON payload.</param>
public sealed record JournalSignalWrite(string Type, ReadOnlyMemory<byte> Json);

/// <summary>The outcome of an append.</summary>
/// <param name="Sequence">The entry sequence: the new ETag of a written record.</param>
/// <param name="LastSignal">The publisher's last signal sequence after the append.</param>
/// <param name="LastPosition">The brain's last signal position after the append.</param>
public sealed record JournalAppendResult(long Sequence, long LastSignal, long LastPosition);

/// <summary>A stored state record as written, with its ETag and state type name.</summary>
/// <param name="Record">The record bytes.</param>
/// <param name="ETag">The sequence of the entry that wrote it.</param>
/// <param name="StateType">The state type name it was written with, or null.</param>
public sealed record JournalStoredState(ReadOnlyMemory<byte> Record, long ETag, string? StateType);

/// <summary>A journaled signal with its raw payload.</summary>
/// <param name="Publisher">The publisher as written (a <see cref="NeuronId"/> string for neuron publishers).</param>
/// <param name="Sequence">The publisher's sequence of the signal.</param>
/// <param name="Position">The brain-wide position of the signal.</param>
/// <param name="Type">The signal type name.</param>
/// <param name="Json">The UTF-8 JSON payload.</param>
/// <param name="At">When the entry was appended.</param>
public sealed record JournalSignalEntry(string Publisher, long Sequence, long Position, string Type, ReadOnlyMemory<byte> Json, DateTimeOffset At);

/// <summary>Held signals after a cursor, with the floor and the last value of that numbering.</summary>
/// <param name="Floor">Everything at or below the floor is trimmed.</param>
/// <param name="Last">The last sequence or position written.</param>
/// <param name="Signals">The held signals after the cursor, in order.</param>
public sealed record JournalSignalPage(long Floor, long Last, IReadOnlyList<JournalSignalEntry> Signals);

/// <summary>
/// Writes and reads a brain's BrainLog for code that is not a DigitalBrain neuron: os's temporary legacy adapters (04-17, R4-10).
/// It shares the per-brain <see cref="BrainLog"/> instance (and its exclusive writer claim) with the runtime through DI, so a brain
/// is only ever written by one log. Entries use the same Json format as every other entry; signals whose type is a registered
/// <c>[Signal]</c> alias reach plain <c>[On]</c> subscribers, others are skipped by them with a logged reason.
/// </summary>
public interface IBrainJournalWriter
{
    /// <summary>Claims and opens the brain's log (its exclusive <c>writer.lock</c>); later calls reuse the same log.</summary>
    /// <exception cref="BrainNotOwnedException">Another writer holds the brain.</exception>
    Task Claim(string brainId, CancellationToken ct);

    /// <summary>
    /// Appends one entry: the optional <paramref name="state"/> change and the <paramref name="signals"/> of
    /// <paramref name="publisher"/>, durable together. Neuron publishers' signals are then announced to subscribers.
    /// </summary>
    /// <exception cref="Orleans.Storage.InconsistentStateException">The record's ETag changed.</exception>
    /// <exception cref="JournalPayloadException">A payload is not JSON, or not valid JSON for its registered <c>[Signal]</c> type.</exception>
    Task<JournalAppendResult> Append(string brainId, JournalStateWrite? state, string publisher, IReadOnlyList<JournalSignalWrite> signals, CancellationToken ct);

    /// <summary>Reads a state record, or null when it does not exist.</summary>
    Task<JournalStoredState?> ReadState(string brainId, string grainId, string stateName, CancellationToken ct);

    /// <summary>Held signals of the whole brain after <paramref name="cursor"/> (brain positions).</summary>
    Task<JournalSignalPage> ReadSince(string brainId, long cursor, int limit, CancellationToken ct);

    /// <summary>Held signals of <paramref name="publisher"/> after <paramref name="cursor"/> (publisher sequences).</summary>
    Task<JournalSignalPage> ReadSince(string brainId, string publisher, long cursor, int limit, CancellationToken ct);
}

internal sealed class BrainJournalWriter(BrainLogs logs, IServiceProvider services) : IBrainJournalWriter
{
    private static readonly JsonDocumentOptions Parse = new() { MaxDepth = 64 };

    public async Task Claim(string brainId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(brainId);
        await logs.For(brainId).WaitAsync(ct);
    }

    public async Task<JournalAppendResult> Append(string brainId, JournalStateWrite? state, string publisher, IReadOnlyList<JournalSignalWrite> signals, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(brainId);
        ArgumentNullException.ThrowIfNull(signals);
        if (state is null && signals.Count == 0) { throw new ArgumentException("An entry needs a state change or signals.", nameof(signals)); }
        if (state is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(state.GrainId, nameof(state));
            ArgumentNullException.ThrowIfNull(state.StateName, nameof(state));
            if (!Enum.IsDefined(state.Change)) { throw new ArgumentOutOfRangeException(nameof(state), "Unknown state change."); }
        }
        var log = await logs.For(brainId).WaitAsync(ct);
        // Validation runs before the append: a payload subscribers could not read never reaches the disk.
        JournaledSignalPayload[] encoded = [.. signals.Select(signal => Encode(log.Payloads, signal))];
        var change = state?.Change switch
        {
            JournalStateChange.Write => BrainRecordChange.Write,
            JournalStateChange.Clear => BrainRecordChange.Clear,
            _ => BrainRecordChange.None,
        };
        var (sequence, last) = await log.AppendEncoded(state?.GrainId ?? "", state?.StateName ?? "", state?.ExpectedETag, change,
            change == BrainRecordChange.Write ? state!.Record.ToArray() : [], state?.StateType, publisher, encoded, null, ct);
        var position = log.LastPosition;
        if (encoded.Length > 0 && NeuronId.TryParse(publisher, out var neuron) && services.GetService<SignalPublisher>() is { } announcer)
        {
            await announcer.Publish(new AppendedSignals(neuron,
                [.. encoded.Select((payload, index) => (last - encoded.Length + 1 + index, payload.Type))]));
        }
        return new JournalAppendResult(sequence, last, position);
    }

    public async Task<JournalStoredState?> ReadState(string brainId, string grainId, string stateName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(brainId);
        var log = await logs.For(brainId).WaitAsync(ct);
        return await log.Read(grainId, stateName) is { } record ? new JournalStoredState(record.Record, record.ETag, record.Type) : null;
    }

    public async Task<JournalSignalPage> ReadSince(string brainId, long cursor, int limit, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        var log = await logs.For(brainId).WaitAsync(ct);
        return Page(await log.BrainSignals(Math.Max(0, cursor), limit, ct));
    }

    public async Task<JournalSignalPage> ReadSince(string brainId, string publisher, long cursor, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(publisher);
        SignalPage.CheckLimit(limit);
        var log = await logs.For(brainId).WaitAsync(ct);
        return Page(await log.PublisherSignals(publisher, Math.Max(0, cursor), limit, ct));
    }

    private static JournalSignalPage Page(StoredSlice slice)
        => new(slice.Floor, slice.Last, [.. slice.Held.Select(item => new JournalSignalEntry(item.Publisher, item.Sequence, item.Position,
            item.Payload.Type, item.Payload.Json, DateTimeOffset.FromUnixTimeMilliseconds(item.AtUnixMs)))]);

    private static JournaledSignalPayload Encode(JournalPayloads payloads, JournalSignalWrite signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentException.ThrowIfNullOrEmpty(signal.Type, nameof(signal));
        var json = signal.Json.ToArray();
        try { using var _ = JsonDocument.Parse(json, Parse); }
        catch (JsonException error) { throw new JournalPayloadException($"Signal '{signal.Type}' is not valid JSON.", error); }
        var payload = new JournaledSignalPayload(signal.Type, json);
        // A registered alias must decode to its [Signal] record, so plain subscribers can always read it.
        if (payloads.Types.TryResolve(signal.Type, out _)) { _ = payloads.DecodeSignal(payload); }
        return payload;
    }
}
