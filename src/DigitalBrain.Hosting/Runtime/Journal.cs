using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain;

internal sealed class Journal
{
    private const string EntriesKey = "journal";
    private const string PositionKey = "journal.sequence";
    private const string CursorKey = "outbox.cursor";
    private const string ProgressKey = "outbox.progress";
    private const string WatermarksKey = "dedup";
    private const string StateKey = "state";
    private const string SchemaKey = "digitalbrain.v3.schema";
    private const int SchemaVersion = 3;

    internal static readonly FrozenSet<string> CoreKeys = new[]
    {
        EntriesKey, PositionKey, CursorKey, ProgressKey, WatermarksKey, StateKey, SchemaKey,
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IDurableList<StoredJournalRecord> entries;
    private readonly IDurableValue<long> position;
    private readonly IDurableValue<long> cursor;
    private readonly IDurableDictionary<string, DeliveryProgress> progress;
    private readonly IDurableDictionary<string, WatermarkEntry> watermarks;
    private readonly IDurableValue<JsonElement> state;
    private readonly IDurableValue<int> schema;

    internal Journal(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        entries = services.GetRequiredKeyedService<IDurableList<StoredJournalRecord>>(EntriesKey);
        position = services.GetRequiredKeyedService<IDurableValue<long>>(PositionKey);
        cursor = services.GetRequiredKeyedService<IDurableValue<long>>(CursorKey);
        progress = services.GetRequiredKeyedService<IDurableDictionary<string, DeliveryProgress>>(ProgressKey);
        watermarks = services.GetRequiredKeyedService<IDurableDictionary<string, WatermarkEntry>>(WatermarksKey);
        state = services.GetRequiredKeyedService<IDurableValue<JsonElement>>(StateKey);
        schema = services.GetRequiredKeyedService<IDurableValue<int>>(SchemaKey);
    }

    internal long LastRecorded { get; private set; }

    internal JsonElement State
    {
        get => state.Value;
        set => state.Value = value;
    }

    internal JsonElement RecordedState { get; private set; }

    internal void MarkRecorded()
    {
        if (schema.Value == 0 && HasLegacyFootprint())
        {
            throw new InvalidOperationException(
                "This neuron has a pre-v3 DigitalBrain journal. Export or migrate it before activating sealed Core.");
        }

        if (schema.Value is not 0 and not SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported DigitalBrain journal schema '{schema.Value}'.");
        }

        LastRecorded = position.Value;
        RecordedState = state.Value;
    }

    internal void SealSchema()
    {
        if (schema.Value is not 0 and not SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported DigitalBrain journal schema '{schema.Value}'.");
        }

        schema.Value = SchemaVersion;
    }

    internal long AppendReceived(
        string synapseKind,
        SynapseOrigin origin,
        SynapseReference? causedBy,
        JsonElement serialization)
    {
        var next = NextPosition();
        entries.Add(new StoredJournalRecord(
            next,
            JournalRecordDirection.Received,
            synapseKind,
            origin,
            causedBy,
            [],
            serialization));
        return next;
    }

    internal long AppendProduced(
        NeuronId source,
        string synapseKind,
        DateTimeOffset occurredAt,
        SynapseOriginAuthority authority,
        SynapseReference? causedBy,
        DeliveryTarget[] deliveryTargets,
        JsonElement serialization)
    {
        var next = NextPosition();
        entries.Add(new StoredJournalRecord(
            next,
            JournalRecordDirection.Produced,
            synapseKind,
            new SynapseOrigin(source, next, occurredAt, authority),
            causedBy,
            deliveryTargets,
            serialization));
        return next;
    }

    internal JournalRead Read(long afterPosition, int maximumRecords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);

        const long availableFromPosition = 1;
        if (afterPosition < availableFromPosition - 1)
        {
            return new JournalHistoryUnavailable(afterPosition, availableFromPosition, LastRecorded);
        }

        if (afterPosition >= LastRecorded)
        {
            return new JournalPage([], afterPosition, LastRecorded);
        }

        var first = afterPosition + 1;
        var last = Math.Min(LastRecorded, afterPosition + maximumRecords);
        var records = new List<JournalRecord>((int)(last - first + 1));
        for (var positionAt = first; positionAt <= last; positionAt++)
        {
            records.Add(entries[(int)(positionAt - 1)].ToJournalRecord());
        }

        return new JournalPage(records, last, LastRecorded);
    }

    internal long WatermarkOf(NeuronId source)
        => watermarks.TryGetValue(NeuronKey.Encode(source), out var mark) ? mark.Position : 0;

    internal void SetWatermark(NeuronId source, long sequence)
        => watermarks[NeuronKey.Encode(source)] = new WatermarkEntry(sequence);

    internal StoredJournalRecord? NextPending()
    {
        AdvanceCursor();
        return cursor.Value < LastRecorded ? EntryAt(cursor.Value + 1) : null;
    }

    internal void SetProgress(long positionAt, DeliveryProgress value) => progress[Key(positionAt)] = value;

    internal DeliveryProgress? ProgressOf(long positionAt)
        => progress.TryGetValue(Key(positionAt), out var value) ? value : null;

    internal void Settle(long positionAt)
    {
        progress[Key(positionAt)] = new DeliveryProgress([], 0);
        AdvanceCursor();
    }

    internal bool HasPending() => HasPendingThrough(LastRecorded);

    internal bool HasUnrecordedPending() => HasPendingThrough(position.Value);

    private bool HasPendingThrough(long ceiling)
    {
        for (var positionAt = cursor.Value + 1; positionAt <= ceiling; positionAt++)
        {
            if (EntryAt(positionAt, ceiling) is { Direction: JournalRecordDirection.Produced, DeliveryTargets.Length: > 0 } entry
                && ProgressOf(entry.Position) is not { Pending.Length: 0 })
            {
                return true;
            }
        }

        return false;
    }

    private void AdvanceCursor()
    {
        while (cursor.Value < LastRecorded)
        {
            var next = cursor.Value + 1;
            var entry = EntryAt(next);
            if (entry is { Direction: JournalRecordDirection.Produced, DeliveryTargets.Length: > 0 }
                && ProgressOf(next) is not { Pending.Length: 0 })
            {
                return;
            }

            progress.Remove(Key(next));
            cursor.Value = next;
        }
    }

    private long NextPosition()
    {
        var next = position.Value + 1;
        position.Value = next;
        return next;
    }

    private static string Key(long positionAt) => positionAt.ToString(CultureInfo.InvariantCulture);

    private StoredJournalRecord? EntryAt(long positionAt, long? ceiling = null)
    {
        var upper = ceiling ?? LastRecorded;
        return positionAt is > 0 && positionAt <= upper ? entries[(int)(positionAt - 1)] : null;
    }

    private bool HasLegacyFootprint()
        => position.Value != 0
            || entries.Count != 0
            || cursor.Value != 0
            || progress.Count != 0
            || watermarks.Count != 0
            || state.Value.ValueKind != JsonValueKind.Undefined;
}
