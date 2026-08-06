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
    private const string SchemaKey = "digitalbrain.v2.schema";
    private const int SchemaVersion = 2;

    internal static readonly FrozenSet<string> CoreKeys = new[]
    {
        EntriesKey, PositionKey, CursorKey, ProgressKey, WatermarksKey, StateKey, SchemaKey,
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IDurableList<JournalEntry> entries;
    private readonly IDurableValue<long> position;
    private readonly IDurableValue<long> cursor;
    private readonly IDurableDictionary<string, DeliveryProgress> progress;
    private readonly IDurableDictionary<string, WatermarkEntry> watermarks;
    private readonly IDurableValue<JsonElement> state;
    private readonly IDurableValue<int> schema;

    internal Journal(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        entries = services.GetRequiredKeyedService<IDurableList<JournalEntry>>(EntriesKey);
        position = services.GetRequiredKeyedService<IDurableValue<long>>(PositionKey);
        cursor = services.GetRequiredKeyedService<IDurableValue<long>>(CursorKey);
        progress = services.GetRequiredKeyedService<IDurableDictionary<string, DeliveryProgress>>(ProgressKey);
        watermarks = services.GetRequiredKeyedService<IDurableDictionary<string, WatermarkEntry>>(WatermarksKey);
        state = services.GetRequiredKeyedService<IDurableValue<JsonElement>>(StateKey);
        schema = services.GetRequiredKeyedService<IDurableValue<int>>(SchemaKey);
    }

    internal long LastCommitted { get; private set; }

    internal JsonElement State
    {
        get => state.Value;
        set => state.Value = value;
    }

    internal JsonElement CommittedState { get; private set; }

    internal void MarkCommitted()
    {
        if (schema.Value == 0 && HasLegacyFootprint())
        {
            throw new InvalidOperationException(
                "This neuron has a pre-v2 DigitalBrain journal. Migrate or export it before activating v2 Core.");
        }

        if (schema.Value is not 0 and not SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported DigitalBrain journal schema '{schema.Value}'.");
        }

        LastCommitted = position.Value;
        CommittedState = state.Value;
    }

    internal void SealV2Schema()
    {
        if (schema.Value is not 0 and not SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported DigitalBrain journal schema '{schema.Value}'.");
        }

        schema.Value = SchemaVersion;
    }

    internal long AppendHeard(
        string kind, DateTimeOffset at, SynapseRefEntry from, SynapseRefEntry? cause, JsonElement body)
    {
        var next = NextPosition();
        entries.Add(new JournalEntry(next, JournalEntry.Heard, kind, at, cause, from, null, body, null));
        return next;
    }

    internal long AppendSaid(
        string kind,
        DateTimeOffset at,
        SynapseRefEntry? cause,
        DeliveryTarget[] to,
        JsonElement body,
        SpeechRole role)
    {
        var next = NextPosition();
        entries.Add(new JournalEntry(next, JournalEntry.Said, kind, at, cause, null, to, body, role));
        return next;
    }

    internal JournalEntry? EntryAt(long at) => EntryAt(at, LastCommitted);

    internal IReadOnlyList<JournalEntry> Read(long afterPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterPosition);
        if (afterPosition >= LastCommitted)
        {
            return [];
        }

        var first = Math.Max(1, afterPosition + 1);
        var read = new List<JournalEntry>((int)(LastCommitted - first + 1));
        for (var at = first; at <= LastCommitted; at++)
        {
            read.Add(entries[(int)(at - 1)]);
        }

        return read;
    }

    internal long WatermarkOf(NeuronId source)
        => watermarks.TryGetValue(NeuronKey.Encode(source), out var mark) ? mark.Position : 0;

    internal void SetWatermark(NeuronId source, long sequence)
        => watermarks[NeuronKey.Encode(source)] = new WatermarkEntry(sequence);

    internal JournalEntry? NextPending()
    {
        AdvanceCursor();
        return cursor.Value < LastCommitted ? EntryAt(cursor.Value + 1) : null;
    }

    internal void SetProgress(long at, DeliveryProgress value) => progress[Key(at)] = value;

    internal DeliveryProgress? ProgressOf(long at)
        => progress.TryGetValue(Key(at), out var value) ? value : null;

    internal void Settle(long at)
    {
        progress[Key(at)] = new DeliveryProgress([], 0);
        AdvanceCursor();
    }

    internal bool HasPending() => HasPendingThrough(LastCommitted);

    internal bool HasUncommittedPending() => HasPendingThrough(position.Value);

    private bool HasPendingThrough(long ceiling)
    {
        for (var at = cursor.Value + 1; at <= ceiling; at++)
        {
            if (EntryAt(at, ceiling) is { Entry: JournalEntry.Said, To.Length: > 0 } entry
                && ProgressOf(entry.Position) is not { Pending.Length: 0 })
            {
                return true;
            }
        }

        return false;
    }

    private void AdvanceCursor()
    {
        while (cursor.Value < LastCommitted)
        {
            var next = cursor.Value + 1;
            var entry = EntryAt(next);
            if (entry is { Entry: JournalEntry.Said, To.Length: > 0 }
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

    private static string Key(long at) => at.ToString(CultureInfo.InvariantCulture);

    private JournalEntry? EntryAt(long at, long ceiling)
        => at is > 0 && at <= ceiling ? entries[(int)(at - 1)] : null;

    private bool HasLegacyFootprint()
        => position.Value != 0
            || entries.Count != 0
            || cursor.Value != 0
            || progress.Count != 0
            || watermarks.Count != 0
            || state.Value.ValueKind != JsonValueKind.Undefined;
}
