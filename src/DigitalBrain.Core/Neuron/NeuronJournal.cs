using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain;

// Committed-only: Orleans.Journaling surfaces uncommitted mutations; reads cap at lastCommitted.
internal sealed class NeuronJournal
{
    private const int EntryOverhead = 128;

    private const string JournalKey = "journal";
    private const string SequenceKey = "journal.sequence";
    private const string CursorKey = "outbox.cursor";
    private const string ProgressKey = "outbox.progress";
    private const string AsksKey = "asks";
    private const string OpenAsksKey = "asks.open";
    private const string DedupKey = "dedup";
    private const string ConnectionsKey = "connections";
    private const string ScheduleKey = "schedule";
    private const string HeardTalliesKey = "tallies.heard";
    private const string SaidTalliesKey = "tallies.said";
    private const string StateKey = "state";

    internal static readonly FrozenSet<string> CoreKeys = new[]
    {
        JournalKey, SequenceKey, CursorKey, ProgressKey, AsksKey, OpenAsksKey,
        DedupKey, ConnectionsKey, ScheduleKey, HeardTalliesKey, SaidTalliesKey, StateKey,
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IDurableList<JournalEntry> journal;
    private readonly IDurableValue<long> lastSeq;
    private readonly IDurableValue<long> cursor;
    private readonly IDurableDictionary<string, DeliveryProgress> progress;
    private readonly IDurableDictionary<string, DateTimeOffset> asks;
    private readonly IDurableDictionary<string, SynapseRefEntry> openAsks;
    private readonly IDurableDictionary<string, WatermarkEntry> dedup;
    private readonly IDurableDictionary<string, NeuronIdEntry[]> connections;
    private readonly IDurableDictionary<string, ScheduleEntry> schedule;
    private readonly IDurableDictionary<string, long> heardTallies;
    private readonly IDurableDictionary<string, long> saidTallies;
    private readonly IDurableValue<JsonElement> state;

    public NeuronJournal(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        journal = services.GetRequiredKeyedService<IDurableList<JournalEntry>>(JournalKey);
        lastSeq = services.GetRequiredKeyedService<IDurableValue<long>>(SequenceKey);
        cursor = services.GetRequiredKeyedService<IDurableValue<long>>(CursorKey);
        progress = services.GetRequiredKeyedService<IDurableDictionary<string, DeliveryProgress>>(ProgressKey);
        asks = services.GetRequiredKeyedService<IDurableDictionary<string, DateTimeOffset>>(AsksKey);
        openAsks = services.GetRequiredKeyedService<IDurableDictionary<string, SynapseRefEntry>>(OpenAsksKey);
        dedup = services.GetRequiredKeyedService<IDurableDictionary<string, WatermarkEntry>>(DedupKey);
        connections = services.GetRequiredKeyedService<IDurableDictionary<string, NeuronIdEntry[]>>(ConnectionsKey);
        schedule = services.GetRequiredKeyedService<IDurableDictionary<string, ScheduleEntry>>(ScheduleKey);
        heardTallies = services.GetRequiredKeyedService<IDurableDictionary<string, long>>(HeardTalliesKey);
        saidTallies = services.GetRequiredKeyedService<IDurableDictionary<string, long>>(SaidTalliesKey);
        state = services.GetRequiredKeyedService<IDurableValue<JsonElement>>(StateKey);
    }

    internal long LastSeq => lastSeq.Value;

    internal long LastCommitted { get; private set; }

    internal long Cursor
    {
        get => cursor.Value;
        set => cursor.Value = value;
    }

    // Positions stable across compaction: head shrinks; lastSeq + Count imply earliest.
    internal long EarliestRetained
        => journal.Count == 0 ? lastSeq.Value + 1 : lastSeq.Value - journal.Count + 1;

    internal JsonElement State
    {
        get => state.Value;
        set => state.Value = value;
    }

    internal JsonElement CommittedState { get; private set; }

    internal IReadOnlyDictionary<string, IReadOnlyList<NeuronId>> CommittedConnections { get; private set; }
        = new Dictionary<string, IReadOnlyList<NeuronId>>(StringComparer.Ordinal);

    internal void MarkCommitted()
    {
        LastCommitted = lastSeq.Value;
        CommittedState = state.Value;
        CommittedConnections = BuildConnectionsSnapshot();
    }

    internal long AppendHeard(
        string kind,
        DateTimeOffset at,
        SynapseRefEntry? from,
        SynapseRefEntry? cause,
        SynapseRefEntry? answers,
        JsonElement body)
    {
        var seq = Mint();
        journal.Add(new JournalEntry(seq, JournalEntry.Heard, kind, at, cause, answers, from, To: null, body));
        heardTallies[kind] = TallyOf(heardTallies, kind) + 1;
        return seq;
    }

    internal long AppendSaid(
        string kind,
        DateTimeOffset at,
        SynapseRefEntry? cause,
        SynapseRefEntry? answers,
        NeuronIdEntry[] to,
        JsonElement body)
    {
        var seq = Mint();
        journal.Add(new JournalEntry(seq, JournalEntry.Said, kind, at, cause, answers, From: null, to, body));
        saidTallies[kind] = TallyOf(saidTallies, kind) + 1;
        return seq;
    }

    internal JournalEntry? EntryAt(long position)
    {
        var earliest = EarliestRetained;

        return position > LastCommitted || position < earliest
            ? null
            : journal[(int)(position - earliest)];
    }

    internal JournalRead Read(long afterPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterPosition);

        var committed = LastCommitted;
        var earliest = EarliestRetained;
        var connectionsSnapshot = CommittedConnections;

        if (afterPosition > committed || afterPosition < earliest - 1)
        {
            return new JournalRead(committed, [], Snapshot(), connectionsSnapshot);
        }

        var delta = new List<JournalEntry>((int)(committed - afterPosition));
        for (var position = afterPosition + 1; position <= committed; position++)
        {
            delta.Add(journal[(int)(position - earliest)]);
        }

        return new JournalRead(committed, delta, Reset: null, connectionsSnapshot);
    }

    internal JournalResetSnapshot Snapshot() => new(
        TotalHeard: heardTallies.Sum(tally => tally.Value),
        TotalSaid: saidTallies.Sum(tally => tally.Value),
        LastSeq: LastCommitted,
        EarliestRetained: EarliestRetained,
        HeardTallies: heardTallies.ToDictionary(tally => tally.Key, tally => tally.Value),
        SaidTallies: saidTallies.ToDictionary(tally => tally.Key, tally => tally.Value));

    internal long WatermarkOf(NeuronId source)
        => dedup.TryGetValue(source.ToString(), out var mark) ? mark.Seq : 0;

    internal void SetWatermark(NeuronId source, long sequence, DateTimeOffset touched)
        => dedup[source.ToString()] = new WatermarkEntry(sequence, touched);

    internal bool PruneWatermarks(DateTimeOffset now, TimeSpan retention)
    {
        var stale = dedup
            .Where(mark => now - mark.Value.Touched > retention)
            .Select(mark => mark.Key)
            .ToArray();
        foreach (var source in stale)
        {
            dedup.Remove(source);
        }

        return stale.Length > 0;
    }

    internal bool AddConnection(string factKind, NeuronId to)
    {
        var row = connections.TryGetValue(factKind, out var existing) ? existing : [];
        if (Array.Exists(row, target => target.ToNeuronId() == to))
        {
            return false;
        }

        connections[factKind] = [.. row, NeuronIdEntry.From(to, via: string.Empty)];
        return true;
    }

    internal bool RemoveConnection(string factKind, NeuronId to)
    {
        if (!connections.TryGetValue(factKind, out var row))
        {
            return false;
        }

        var remaining = Array.FindAll(row, target => target.ToNeuronId() != to);
        if (remaining.Length == row.Length)
        {
            return false;
        }

        if (remaining.Length == 0)
        {
            connections.Remove(factKind);
        }
        else
        {
            connections[factKind] = remaining;
        }

        return true;
    }

    internal IReadOnlyList<NeuronId> ConnectionsOf(string factKind)
        => connections.TryGetValue(factKind, out var row)
            ? [.. row.Select(target => target.ToNeuronId())]
            : [];

    private Dictionary<string, IReadOnlyList<NeuronId>> BuildConnectionsSnapshot()
        => connections.ToDictionary(
            row => row.Key,
            row => (IReadOnlyList<NeuronId>)[.. row.Value.Select(target => target.ToNeuronId())],
            StringComparer.Ordinal);

    internal ScheduleEntry? ScheduleOf(string factKind)
        => schedule.TryGetValue(factKind, out var entry) ? entry : null;

    internal void SetSchedule(string factKind, ScheduleEntry entry) => schedule[factKind] = entry;

    internal bool RemoveSchedule(string factKind) => schedule.Remove(factKind);

    internal IReadOnlyDictionary<string, ScheduleEntry> ScheduleSnapshot()
        => schedule.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    internal bool HasAskPins => asks.Count > 0;

    internal bool HasSchedules => schedule.Count > 0;

    internal void PinAsk(long position, DateTimeOffset askedAt) => asks[Key(position)] = askedAt;

    internal bool UnpinAsk(long position) => asks.Remove(Key(position));

    internal bool HasAskPin(long position) => asks.TryGetValue(Key(position), out _);

    internal SynapseRefEntry? OpenAskOf(string questionKind)
        => openAsks.TryGetValue(questionKind, out var ask) ? ask : null;

    internal void SetOpenAsk(string questionKind, SynapseRefEntry ask) => openAsks[questionKind] = ask;

    internal bool RemoveOpenAsk(string questionKind) => openAsks.Remove(questionKind);

    internal List<KeyValuePair<string, SynapseRefEntry>> OpenAsksSnapshot()
        => [.. openAsks.OrderBy(ask => ask.Key, StringComparer.Ordinal)];

    internal IReadOnlyList<long> ExpiredAsks(DateTimeOffset now, TimeSpan horizon)
        => [.. asks
            .Where(pin => now - pin.Value > horizon)
            .Select(pin => PositionOf(pin.Key))
            .Order()];

    internal long? OldestAskPin()
    {
        long? oldest = null;
        foreach (var pin in asks)
        {
            var position = PositionOf(pin.Key);
            if (oldest is null || position < oldest)
            {
                oldest = position;
            }
        }

        return oldest;
    }

    // Absent progress = untouched; map holds progress only, never payload.
    internal DeliveryProgress? ProgressOf(long position)
        => progress.TryGetValue(Key(position), out var partial) ? partial : null;

    internal void SetProgress(long position, DeliveryProgress value) => progress[Key(position)] = value;

    internal bool ClearProgress(long position) => progress.Remove(Key(position));

    // Floor is hard (cursor / oldest ask pin / floorLimit); retained bounds are soft only.
    internal void Compact(long floorLimit)
    {
        var floor = Math.Min(Math.Min(cursor.Value, OldestAskPin() ?? long.MaxValue), floorLimit);
        var retainedBytes = journal.Sum(EstimatedSize);

        while (journal.Count > 0
            && journal[0].Seq < floor
            && (journal.Count > DeliveryPolicy.MaxRetainedEntries
                || retainedBytes > DeliveryPolicy.MaxRetainedBytes))
        {
            retainedBytes -= EstimatedSize(journal[0]);
            journal.RemoveAt(0);
        }
    }

    private long Mint()
    {
        var seq = lastSeq.Value + 1;
        lastSeq.Value = seq;
        return seq;
    }

    private static long TallyOf(IDurableDictionary<string, long> tallies, string kind)
        => tallies.TryGetValue(kind, out var recorded) ? recorded : 0;

    private static long EstimatedSize(JournalEntry entry)
        => entry.Body.ValueKind == JsonValueKind.Undefined
            ? EntryOverhead
            : entry.Body.GetRawText().Length + EntryOverhead;

    private static string Key(long position) => position.ToString(CultureInfo.InvariantCulture);

    private static long PositionOf(string key) => long.Parse(key, CultureInfo.InvariantCulture);
}

internal sealed record JournalRead(
    long LastSeq,
    IReadOnlyList<JournalEntry> Delta,
    JournalResetSnapshot? Reset,
    IReadOnlyDictionary<string, IReadOnlyList<NeuronId>> Connections);

internal sealed record JournalResetSnapshot(
    long TotalHeard,
    long TotalSaid,
    long LastSeq,
    long EarliestRetained,
    IReadOnlyDictionary<string, long> HeardTallies,
    IReadOnlyDictionary<string, long> SaidTallies);
