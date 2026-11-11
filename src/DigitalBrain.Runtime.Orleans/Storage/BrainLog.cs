using System.Buffers.Binary;
using System.Globalization;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Synapses;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// One brain's journal (format <see cref="BrainLogFormat.Json"/>). One writer, proven by its ownership lease.
/// State and the signals emitted with it share one flushed, authenticated entry, so a signal is durable before
/// any observer can see it. Each publisher numbers its signals from 1; every signal also has a brain-wide position.
/// Checkpoints trim each publisher below the floor of the <see cref="IRetentionPolicy"/>.
/// </summary>
internal sealed class BrainLog : IAsyncDisposable, IJournalView
{
    private const int MaxEntryBytes = 16 * 1024 * 1024;
    private const int SegmentHeaderBytes = 5;
    private static ReadOnlySpan<byte> SegmentMagic => "DBLG"u8;
    private readonly string directory;
    private readonly string brainId;
    private readonly byte[] key;
    private readonly Serializer serializer;
    private readonly JournalPayloads payloads;
    private readonly TimeProvider time;
    private readonly IRetentionPolicy retention;
    private readonly IBrainLogLease ownership;
    private readonly IBrainLogFiles files;
    private readonly BrainStoreOptions options;
    // Held across each durable write and checkpoint to guarantee contiguous sequence and file updates.
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim checkpointGate = new(1, 1);
    // Protects in-memory projections (records, signals, marks, sequence, position) from concurrent access without blocking on disk flushes.
    private readonly Lock stateLock = new();
    private readonly Journal<JournaledSignalPayload> _journal = new();
    private readonly SubscriptionIndex _subscriptions = new();
    private string generation = "initial";
    private long uncompactedBytes;
    private int uncompactedEntries;
    // Read without the gate; written only under it.
    private volatile Exception? failure;
    private volatile bool disposed;

    public DigitalBrain.Kernel.Fanout? Fanout { get; set; }

    internal BrainLog(string directory, string brainId, byte[] key, Serializer serializer, JournalPayloads payloads, TimeProvider time,
        IRetentionPolicy retention, IBrainLogLease ownership, IBrainLogFiles? files = null, BrainStoreOptions? options = null)
    {
        this.ownership = ownership;
        this.files = files ?? FileBrainLogFiles.Instance;
        this.options = options ?? new BrainStoreOptions();
        this.directory = directory;
        this.brainId = brainId;
        this.key = key;
        this.serializer = serializer;
        this.payloads = payloads;
        this.time = time;
        this.retention = retention;
        try
        {
            Recover();
            TryTriggerAutoCheckpoint();
        }
        catch { ReleaseUnopened(ownership); throw; }
    }

    public string BrainId => brainId;

    public JournalPayloads Payloads => payloads;

    public long Sequence
    {
        get
        {
            lock (stateLock) { return _journal.Sequence; }
        }
    }

    /// <summary>The last brain-wide signal position.</summary>
    public long LastPosition
    {
        get
        {
            lock (stateLock) { return _journal.Position; }
        }
    }

    public Position Last => new(LastPosition);

    public SubscriptionIndex Subscriptions => _subscriptions;

    public Position FloorOf(NeuronId? publisher)
    {
        lock (stateLock)
        {
            return publisher is { } p ? new Position(_journal.FloorOf(p.ToString())) : new Position(0);
        }
    }

    public Position LastOf(NeuronId publisher)
    {
        lock (stateLock)
        {
            return new Position(_journal.LastPositionOf(publisher.ToString()));
        }
    }

    public IEnumerable<AliasMark> Marks(NeuronId? publisher)
    {
        lock (stateLock)
        {
            var pubString = publisher?.ToString();
            var dict = new Dictionary<(string Publisher, string Alias), Position>();
            foreach (var entry in _journal.HeldEntries)
            {
                if (pubString is null || entry.Publisher == pubString)
                {
                    for (var i = 0; i < entry.Signals.Count; i++)
                    {
                        var payload = entry.Signals[i];
                        dict[(entry.Publisher, payload.Type)] = new Position(entry.FirstPosition + i);
                    }
                }
            }
            return dict.Select(kv => new AliasMark(NeuronId.Parse(kv.Key.Publisher), kv.Key.Alias, kv.Value)).ToList();
        }
    }

    public long SequenceAt(NeuronId publisher, Position position)
    {
        lock (stateLock)
        {
            return _journal.SequenceAt(publisher.ToString(), position.Value);
        }
    }

    public Position PositionAt(NeuronId publisher, long sequence)
    {
        lock (stateLock)
        {
            var pos = _journal.PositionAt(publisher.ToString(), sequence);
            return pos > 0 ? new Position(pos) : new Position(sequence);
        }
    }

    public Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct)
    {
        RequireOpen();
        ct.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            var pubString = publisher?.ToString();
            var held = _journal.HeldAfter(after.Value, pubString, limit);
            var floor = FloorOf(publisher);
            var positioned = new List<PositionedSignal>();
            foreach (var entry in held)
            {
                for (var i = 0; i < entry.Signals.Count; i++)
                {
                    var pos = new Position(entry.FirstPosition + i);
                    if (pos.Value <= after.Value) { continue; }
                    var payload = entry.Signals[i];
                    var pubId = NeuronId.Parse(entry.Publisher);
                    var signalObj = Decode(payload);
                    var seq = publisher is null ? (entry.FirstPosition + i) : (entry.FirstSignal + i);
                    var jSignal = new JournaledSignal(pubId, seq, signalObj,
                        DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs), payload.TraceId, payload.CausationId);
                    positioned.Add(new PositionedSignal(pos, payload.Type, jSignal));
                }
            }
            return Task.FromResult(new JournalPage(floor, Last, positioned.Take(limit).ToList()));
        }
    }

    private Signal Decode(JournaledSignalPayload payload)
    {
        if (payloads.Types.TryResolve(payload.Type, out _)) { return payloads.DecodeSignal(payload); }
        return new UnknownSignal(payload.Type) { Json = payload.Json };
    }

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
    }

    internal async Task<bool> CheckStorage(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RequireOpen();
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, ".readiness-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var probe = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                    4096, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                probe.WriteByte(0x5A);
                probe.Flush(flushToDisk: true);
                probe.Position = 0;
                return probe.ReadByte() == 0x5A;
            }
            finally { File.Delete(path); }
        }
        finally { gate.Release(); }
    }

    public Task<BrainRecord?> Read(string grainId, string stateName)
    {
        RequireOpen();
        lock (stateLock)
        {
            var record = _journal.Record(grainId, stateName);
            return Task.FromResult(record is null ? null : new BrainRecord(record.Bytes.ToArray(), record.Version, record.Type));
        }
    }

    public JournalRecord? Record(string grainId, string stateName)
    {
        RequireOpen();
        lock (stateLock)
        {
            return _journal.Record(grainId, stateName);
        }
    }

    /// <summary>Every stored record of <paramref name="stateName"/>, grain id to a copy of the bytes.</summary>
    internal List<(string Grain, byte[] Record)> Named(string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        RequireOpen();
        lock (stateLock)
        {
            var found = new List<(string, byte[])>();
            foreach (var (grain, record) in _journal.Named(stateName))
            {
                found.Add((grain, record.Bytes.ToArray()));
            }
            return found;
        }
    }

    public async Task<JournalEntry<JournaledSignalPayload>> Append(
        string grainId,
        RecordWrite? state,
        RecordWrite? side,
        string publisher,
        JournaledSignalPayload[] encoded,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length > 0) { ArgumentException.ThrowIfNullOrEmpty(publisher); }
        encoded = Stamped(encoded);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            RequireOpen();
            ct.ThrowIfCancellationRequested();
            JournalEntry<JournaledSignalPayload> entry;
            lock (stateLock)
            {
                entry = _journal.Decide(grainId, state, side, publisher, encoded, time.GetUtcNow().ToUnixTimeMilliseconds());
            }
            var brainEntry = new BrainEntry(
                entry.Sequence,
                entry.Owner,
                entry.State?.Name ?? "",
                entry.State is not null ? ToRecordChange(entry.State.Change) : BrainRecordChange.None,
                entry.State?.Bytes.ToArray() ?? [],
                entry.Publisher,
                encoded,
                entry.FirstSignal,
                entry.FirstPosition,
                entry.AtUnixMs,
                entry.Side?.Name,
                entry.Side?.Bytes.ToArray(),
                entry.State?.Type);

            var plain = serializer.SerializeToArray(brainEntry);
            if (plain.Length > MaxEntryBytes) { throw new ArgumentException("A brain change exceeds 16 MiB.", nameof(state)); }
            var bytes = SegmentSeal.Close(key, Context("log"), plain);
            var started = ownership.BeginWrite();
            try
            {
                using var stream = files.OpenAppend(Journal);
                var before = stream.Length;
                var headed = stream.Length == 0;
                var frame = new byte[(headed ? SegmentHeaderBytes : 0) + 4 + bytes.Length];
                var offset = 0;
                if (headed) { WriteHeader(frame); offset = SegmentHeaderBytes; }
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset), bytes.Length);
                bytes.CopyTo(frame.AsSpan(offset + 4));
                stream.Write(frame);
                try { ownership.EndWrite(started); }
                catch (BrainNotOwnedException)
                {
                    stream.SetLength(before);
                    FlushToDisk(stream);
                    throw;
                }
                FlushToDisk(stream);
                lock (stateLock)
                {
                    _journal.Apply(entry);
                    FoldSubscriptions(entry);
                    uncompactedBytes += frame.Length;
                    uncompactedEntries++;
                }
            }
            catch (Exception error) { failure = error; throw; }
            return entry;
        }
        finally
        {
            gate.Release();
            TryTriggerAutoCheckpoint();
        }
    }

    /// <summary>
    /// Appends one entry: the state change of <paramref name="grainId"/> and the <paramref name="emitted"/> signals of
    /// <paramref name="publisher"/>. Returns the entry sequence (a record's ETag) and the publisher's last signal sequence.
    /// </summary>
    public Task<(long Sequence, long LastSignal)> AppendEntry(string grainId, string stateName, long? expectedETag, BrainRecordChange change,
        byte[] record, string publisher, IReadOnlyList<Signal> emitted, CancellationToken ct)
        => AppendEntry(grainId, stateName, expectedETag, change, record, publisher, emitted, null, ct);

    /// <summary>
    /// Appends one entry as above that also writes <paramref name="side"/>, a second state of <paramref name="grainId"/>, in the same
    /// durable write: both or neither (R4-09).
    /// </summary>
    public async Task<(long Sequence, long LastSignal)> AppendEntry(string grainId, string stateName, long? expectedETag, BrainRecordChange change,
        byte[] record, string publisher, IReadOnlyList<Signal> emitted, BrainSideWrite? side, CancellationToken ct)
    {
        var (sequence, lastSignal, _) = await AppendEntryWithPosition(grainId, stateName, expectedETag, change, record, publisher, emitted, side, ct).ConfigureAwait(false);
        return (sequence, lastSignal);
    }

    internal async Task<(long Sequence, long LastSignal, long LastPosition)> AppendEntryWithPosition(string grainId, string stateName, long? expectedETag, BrainRecordChange change,
        byte[] record, string publisher, IReadOnlyList<Signal> emitted, BrainSideWrite? side, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(emitted);
        // Encoding runs before the gate: a payload that cannot be journaled never reaches the disk.
        JournaledSignalPayload[] encoded = [.. emitted.Select(payloads.EncodeSignal)];
        return await AppendEncodedWithPosition(grainId, stateName, expectedETag, change, record, null, publisher, encoded, side, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Appends one entry whose signals are already encoded (alias plus JSON) and whose record is opaque, optionally naming its state
    /// type: the write path of <see cref="IBrainJournalWriter"/> (04-17), in the same format as every other entry.
    /// </summary>
    internal async Task<(long Sequence, long LastSignal)> AppendEncoded(string grainId, string stateName, long? expectedETag, BrainRecordChange change,
        byte[] record, string? recordType, string publisher, JournaledSignalPayload[] encoded, BrainSideWrite? side, CancellationToken ct)
    {
        var (sequence, lastSignal, _) = await AppendEncodedWithPosition(grainId, stateName, expectedETag, change, record, recordType, publisher, encoded, side, ct).ConfigureAwait(false);
        return (sequence, lastSignal);
    }

    // Every signal of one entry shares the entry's trace and cause; a payload an adapter already stamped keeps its own ids.
    private static JournaledSignalPayload[] Stamped(JournaledSignalPayload[] encoded)
    {
        if (encoded.Length == 0 || encoded.All(payload => payload.TraceId is not null)) { return encoded; }
        var (traceId, causationId) = CurrentCause.Stamp();
        return [.. encoded.Select(payload => payload.TraceId is null ? payload with { TraceId = traceId, CausationId = causationId } : payload)];
    }

    private async Task<(long Sequence, long LastSignal, long LastPosition)> AppendEncodedWithPosition(string grainId, string stateName, long? expectedETag, BrainRecordChange change,
        byte[] record, string? recordType, string publisher, JournaledSignalPayload[] encoded, BrainSideWrite? side, CancellationToken ct)
    {
        RecordWrite? stateWrite = change == BrainRecordChange.None && string.IsNullOrEmpty(stateName)
            ? null
            : new RecordWrite(stateName, ToStateChange(change), record, expectedETag, recordType);
        RecordWrite? sideWrite = side is null
            ? null
            : new RecordWrite(side.StateName, StateChange.Write, side.Record, side.ExpectedETag);
        try
        {
            var entry = await Append(grainId, stateWrite, sideWrite, publisher, encoded, ct).ConfigureAwait(false);
            long lastSignal;
            lock (stateLock)
            {
                lastSignal = encoded.Length > 0 ? _journal.LastOf(publisher) : _journal.LastOf(publisher);
            }
            return (entry.Sequence, lastSignal, _journal.Position);
        }
        catch (StaleVersionException ex)
        {
            throw new InconsistentStateException(ex.Message,
                ex.Stored?.ToString(CultureInfo.InvariantCulture) ?? "none",
                ex.Expected?.ToString(CultureInfo.InvariantCulture) ?? "none");
        }
    }

    private static bool TryParseNeuronId(string text, out NeuronId id)
    {
        if (text.StartsWith("neuron.host/", StringComparison.Ordinal))
        {
            return NeuronId.TryParse(text["neuron.host/".Length..], out id);
        }
        if (NeuronId.TryParse(text, out id)) { return true; }
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0 && NeuronId.TryParse(text[(slash + 1)..], out id)) { return true; }
        id = default;
        return false;
    }

    private void FoldSubscriptions(JournalEntry<JournaledSignalPayload> entry)
    {
        if (entry.Side is { Name: DigitalBrain.Kernel.Subscriptions.RecordName } sideSub)
        {
            if (TryParseNeuronId(entry.Owner, out var subscriber))
            {
                _subscriptions.Fold(subscriber, sideSub.Bytes, StateChange.Write, owner: entry.Owner);
            }
        }
        else if (entry.Side is { Name: "synapses" } sideLegacy)
        {
            if (TryParseNeuronId(entry.Owner, out var subscriber)
                && SubscriptionUpgrade.TryUpgrade(subscriber, sideLegacy.Bytes, DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs)) is { } upgraded)
            {
                _subscriptions.Fold(upgraded, owner: entry.Owner);
            }
        }

        if (entry.State is { Name: DigitalBrain.Kernel.Subscriptions.RecordName } stateSub)
        {
            if (TryParseNeuronId(entry.Owner, out var subscriber))
            {
                _subscriptions.Fold(subscriber, stateSub.Bytes, stateSub.Change, owner: entry.Owner);
            }
        }
        else if (entry.State is { Name: "synapses" } stateLegacy)
        {
            if (TryParseNeuronId(entry.Owner, out var subscriber)
                && SubscriptionUpgrade.TryUpgrade(subscriber, stateLegacy.Bytes, DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs)) is { } upgraded)
            {
                _subscriptions.Fold(upgraded, owner: entry.Owner);
            }
        }
    }

    /// <summary>Held signals of <paramref name="publisher"/> after <paramref name="since"/> (publisher sequences), with its floor and last.</summary>
    public Task<StoredSlice> PublisherSignals(string publisher, long since, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ct.ThrowIfCancellationRequested();
        RequireOpen();
        lock (stateLock)
        {
            var mark = _journal.Marks.GetValueOrDefault(publisher);
            StoredSignal[] held = [.. Held().Where(item => item.Publisher == publisher && item.Sequence > since).Take(limit)];
            return Task.FromResult(new StoredSlice(mark.Floor, mark.Last, held));
        }
    }

    /// <summary>Held signals of the whole brain after <paramref name="since"/> (brain positions).</summary>
    public Task<StoredSlice> BrainSignals(long since, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ct.ThrowIfCancellationRequested();
        RequireOpen();
        lock (stateLock)
        {
            var firstEntry = _journal.HeldEntries.Count > 0 ? _journal.HeldEntries[0] : null;
            var floor = firstEntry is null ? _journal.Position : firstEntry.FirstPosition - 1;
            StoredSignal[] held = [.. Held().Where(item => item.Position > since).Take(limit)];
            return Task.FromResult(new StoredSlice(floor, _journal.Position, held));
        }
    }

    /// <summary>Held signals of the whole brain carrying <paramref name="traceId"/> after brain position <paramref name="since"/>.</summary>
    public Task<StoredSlice> TraceSignals(string traceId, long since, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(traceId);
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ct.ThrowIfCancellationRequested();
        RequireOpen();
        lock (stateLock)
        {
            var firstEntry = _journal.HeldEntries.Count > 0 ? _journal.HeldEntries[0] : null;
            var floor = firstEntry is null ? _journal.Position : firstEntry.FirstPosition - 1;
            StoredSignal[] held = [.. Held().Where(item => item.Position > since && item.Payload.TraceId == traceId).Take(limit)];
            return Task.FromResult(new StoredSlice(floor, _journal.Position, held));
        }
    }

    // The caller holds the stateLock.
    private IEnumerable<StoredSignal> Held()
        => _journal.HeldEntries.SelectMany(entry => entry.Signals.Select((payload, index)
            => new StoredSignal(entry.Publisher, entry.FirstSignal + index, entry.FirstPosition + index, entry.AtUnixMs, payload)));

    /// <summary>
    /// Writes a new generation: the snapshot of every record and the held signals, after raising each publisher's floor to what the
    /// retention policy allows. The policy runs outside the gate (it may read cursors that persist in this same log).
    /// </summary>
    public async Task Checkpoint(CancellationToken ct)
    {
        await checkpointGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var floors = await Floors(ct).ConfigureAwait(false);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                RequireOpen();
                ct.ThrowIfCancellationRequested();
                var started = ownership.BeginWrite();
                var previous = generation;
                var next = Guid.NewGuid().ToString("N");
                byte[] plain;
                lock (stateLock)
                {
                    foreach (var (publisher, floor) in floors) { Trim(publisher, floor); }
                    var recordsSnapshot = new Dictionary<string, Dictionary<string, BrainRecord>>(StringComparer.Ordinal);
                    foreach (var (owner, dict) in _journal.SnapshotRecords())
                    {
                        var inner = new Dictionary<string, BrainRecord>(StringComparer.Ordinal);
                        foreach (var (name, jr) in dict)
                        {
                            inner[name] = new BrainRecord(jr.Bytes.ToArray(), jr.Version, jr.Type);
                        }
                        recordsSnapshot[owner] = inner;
                    }
                    BrainEntry[] heldSignals = [.. _journal.HeldEntries.Select(e => new BrainEntry(
                        e.Sequence, "", "", BrainRecordChange.None, [], e.Publisher,
                        e.Signals is JournaledSignalPayload[] arr ? arr : [.. e.Signals],
                        e.FirstSignal, e.FirstPosition, e.AtUnixMs))];
                    PublisherMark[] publisherMarks = [.. _journal.SnapshotMarks()
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new PublisherMark(pair.Key, pair.Value.Floor, pair.Value.Last, pair.Value.LastPosition))];

                    var snapshot = new BrainSnapshot(_journal.Sequence, _journal.Position, recordsSnapshot, heldSignals, publisherMarks);
                    var body = serializer.SerializeToArray(snapshot);
                    plain = new byte[SegmentHeaderBytes + body.Length];
                    WriteHeader(plain);
                    body.CopyTo(plain.AsSpan(SegmentHeaderBytes));
                }
                WriteFlushed(Path.Combine(directory, next + ".snapshot"), SegmentSeal.Close(key, Context("snapshot", next), plain));
                WriteFlushed(Path.Combine(directory, next + ".log"), []);
                // A writer-unique staging name, so a stale owner cannot swap its manifest in under the current owner's move.
                var staged = Path.Combine(directory, "current." + next + ".tmp");
                WriteFlushed(staged, System.Text.Encoding.UTF8.GetBytes(next));
                try { ownership.EndWrite(started); File.Move(staged, Path.Combine(directory, "current"), overwrite: true); }
                catch (Exception error)
                {
                    failure = error;
                    TryDelete(staged); // Never read: only "current" names a generation.
                    throw;
                }
                lock (stateLock)
                {
                    generation = next;
                    uncompactedBytes = 0;
                    uncompactedEntries = 0;
                }
                // Old complete generation remains until the manifest points to the new one.
                foreach (var suffix in new[] { ".snapshot", ".log" })
                {
                    // Unreachable generations may be removed on a later maintenance run.
                    TryDelete(Path.Combine(directory, previous + suffix));
                }
            }
            finally { gate.Release(); }
        }
        finally { checkpointGate.Release(); }
    }

    private async Task<List<(string Publisher, long Floor)>> Floors(CancellationToken ct)
    {
        List<(string Publisher, NeuronId Id, long Last, long[] At)> inputs = [];
        RequireOpen();
        lock (stateLock)
        {
            var held = Held().GroupBy(item => item.Publisher, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(item => item.AtUnixMs).ToArray(), StringComparer.Ordinal);
            foreach (var (publisher, range) in _journal.Marks)
            {
                // Only neuron publishers have synapses and cursors; anything else keeps its whole history.
                if (range.Last <= range.Floor || !NeuronId.TryParse(publisher, out var id)) { continue; }
                inputs.Add((publisher, id, range.Last, held.GetValueOrDefault(publisher) ?? []));
            }
        }
        var now = time.GetUtcNow();
        List<(string, long)> floors = [];
        foreach (var (publisher, id, last, at) in inputs)
        {
            // Entries with unknown time (0) count as newer.
            long CountNewerThan(TimeSpan age)
            {
                var cutoff = (now - age).ToUnixTimeMilliseconds();
                return at.LongCount(value => value == 0 || value > cutoff);
            }
            floors.Add((publisher, await retention.FloorFor(brainId, id, last, CountNewerThan, ct)));
        }
        return floors;
    }

    // The caller holds the gate. Floors only rise and never pass the publisher's last sequence.
    private void Trim(string publisher, long floor)
    {
        var mark = _journal.Marks.GetValueOrDefault(publisher);
        floor = Math.Min(floor, mark.Last);
        if (floor <= mark.Floor) { return; }
        long floorPos = 0;
        foreach (var entry in _journal.HeldEntries)
        {
            if (entry.Publisher == publisher && entry.FirstSignal <= floor)
            {
                var drop = Math.Min(entry.Signals.Count, floor - entry.FirstSignal + 1);
                floorPos = entry.FirstPosition + drop - 1;
            }
        }
        _journal.Trim(publisher, floor, floorPos);
    }

    private string Journal => Path.Combine(directory, generation + ".log");
    private string Context(string kind, string? version = null) => $"brain-store|{brainId}|{version ?? generation}|{kind}";

    private void Recover()
    {
        var manifest = Path.Combine(directory, "current");
        if (File.Exists(manifest))
        {
            generation = File.ReadAllText(manifest);
            if (!Guid.TryParseExact(generation, "N", out _)) { throw new InvalidDataException("Invalid brain checkpoint generation."); }
            var plain = SegmentSeal.Open(key, Context("snapshot"), File.ReadAllBytes(Path.Combine(directory, generation + ".snapshot")));
            RequireHeader(plain, plain.Length);
            var checkpoint = serializer.Deserialize<BrainSnapshot>(plain.AsSpan(SegmentHeaderBytes).ToArray())!;
            RestoreSnapshot(checkpoint);
            if (!File.Exists(Journal)) { throw new InvalidDataException("The checkpoint journal is missing."); }
        }
        else if (!File.Exists(Journal) && (Directory.EnumerateFiles(directory, "*.snapshot").Any()
            || Directory.EnumerateFiles(directory, "*.log").Any()))
        { throw new InvalidDataException("The brain checkpoint manifest is missing; refusing to reset existing data."); }
        using var stream = files.OpenRead(Journal);
        long valid = DetectFormat(stream);
        Span<byte> header = stackalloc byte[4];
        while (stream.Position < stream.Length)
        {
            if (stream.Length - stream.Position < 4) { break; }
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length < 28 || length > MaxEntryBytes + 28) { throw new InvalidDataException("Invalid encrypted entry length."); }
            if (stream.Length - stream.Position < length) { break; }
            var bytes = new byte[length];
            stream.ReadExactly(bytes);
            var entry = serializer.Deserialize<BrainEntry>(SegmentSeal.Open(key, Context("log"), bytes))!;
            if (entry.Sequence != _journal.Sequence + 1 || entry.FirstPosition != _journal.Position + 1
                || (entry.Signals.Length > 0 && entry.FirstSignal != _journal.LastOf(entry.Publisher) + 1))
            { throw new InvalidDataException("The local brain log is not contiguous."); }
            Apply(entry);
            valid = stream.Position;
            uncompactedEntries++;
        }
        if (valid != stream.Length) { stream.SetLength(valid); FlushToDisk(stream); }
        uncompactedBytes = valid;
    }

    // Held signals of each publisher run contiguously from Floor + 1 to Last; positions rise strictly.
    private void RestoreSnapshot(BrainSnapshot checkpoint)
    {
        var journalRecords = new Dictionary<string, Dictionary<string, JournalRecord>>(StringComparer.Ordinal);
        foreach (var (grain, states) in checkpoint.Records)
        {
            var inner = new Dictionary<string, JournalRecord>(StringComparer.Ordinal);
            foreach (var (stateName, br) in states)
            {
                inner[stateName] = new JournalRecord(br.Record, br.ETag, br.Type);
            }
            journalRecords[grain] = inner;
        }

        var marks = new Dictionary<string, PublisherRange>(StringComparer.Ordinal);
        foreach (var mark in checkpoint.Publishers)
        {
            if (mark.Floor < 0 || mark.Floor > mark.Last) { throw new InvalidDataException("A snapshot publisher floor is out of range."); }
            marks[mark.Publisher] = new PublisherRange(mark.Floor, mark.Last, mark.LastPosition);
        }

        var heldEntries = new List<JournalEntry<JournaledSignalPayload>>();
        var next = marks.ToDictionary(pair => pair.Key, pair => pair.Value.Floor + 1, StringComparer.Ordinal);
        long lastPosition = 0;
        foreach (var entry in checkpoint.Signals)
        {
            if (!next.TryGetValue(entry.Publisher, out var expected) || entry.FirstSignal != expected || entry.FirstPosition <= lastPosition)
            { throw new InvalidDataException("The brain snapshot signals are not contiguous above their floor."); }
            next[entry.Publisher] = expected + entry.Signals.Length;
            lastPosition = entry.FirstPosition + entry.Signals.Length - 1;
            heldEntries.Add(new JournalEntry<JournaledSignalPayload>(
                entry.Sequence,
                "",
                null,
                null,
                entry.Publisher,
                entry.Signals,
                entry.FirstSignal,
                entry.FirstPosition,
                entry.AtUnixMs));
        }
        if (lastPosition > checkpoint.Position || next.Any(pair => pair.Value != marks[pair.Key].Last + 1))
        { throw new InvalidDataException("The brain snapshot signals do not reach their publishers' last sequence."); }

        _journal.Restore(checkpoint.Sequence, checkpoint.Position, journalRecords, marks, heldEntries);

        foreach (var (grain, states) in journalRecords)
        {
            if (states.TryGetValue(DigitalBrain.Kernel.Subscriptions.RecordName, out var subRecord))
            {
                if (TryParseNeuronId(grain, out var subscriber))
                {
                    _subscriptions.Fold(subscriber, subRecord.Bytes, StateChange.Write, owner: grain);
                }
            }
            else if (states.TryGetValue("synapses", out var legacyRecord))
            {
                if (TryParseNeuronId(grain, out var subscriber)
                    && SubscriptionUpgrade.TryUpgrade(subscriber, legacyRecord.Bytes, DateTimeOffset.UnixEpoch) is { } upgraded)
                {
                    _subscriptions.Fold(upgraded, owner: grain);
                }
            }
        }
    }

    private static void WriteHeader(Span<byte> target)
    {
        SegmentMagic.CopyTo(target);
        target[4] = (byte)BrainLogFormat.Json;
    }

    // SAFE-01 clean break: a snapshot or segment written before the Json format has no header or another version; it is refused, never partially read.
    private void RequireHeader(ReadOnlySpan<byte> bytes, int read)
    {
        var magic = Math.Min(read, SegmentMagic.Length);
        if (read < SegmentHeaderBytes || !bytes[..magic].SequenceEqual(SegmentMagic[..magic]))
        { throw BrainLogFormatException.Predates(BrainLogFormat.Legacy.ToString(), directory); }
        if (bytes[4] != (byte)BrainLogFormat.Json) { throw BrainLogFormatException.Predates(FormatName(bytes[4]), directory); }
    }

    private static string FormatName(byte value)
        => Enum.IsDefined((BrainLogFormat)value) ? ((BrainLogFormat)value).ToString() : "version " + value;

    // os Legacy segments start with a length below 16 MiB, so they never begin with "DBLG"; Framed carries version 1.
    private long DetectFormat(Stream stream)
    {
        Span<byte> header = stackalloc byte[SegmentHeaderBytes];
        var read = stream.ReadAtLeast(header, SegmentHeaderBytes, throwOnEndOfStream: false);
        stream.Position = 0;
        if (read == 0) { return 0; }
        var magic = Math.Min(read, SegmentMagic.Length);
        if (!header[..magic].SequenceEqual(SegmentMagic[..magic]))
        { throw BrainLogFormatException.Predates(BrainLogFormat.Legacy.ToString(), directory); }
        if (read < SegmentHeaderBytes)
        {
            // A torn header holds no entries.
            stream.SetLength(0);
            FlushToDisk(stream);
            return 0;
        }
        RequireHeader(header, read);
        stream.Position = SegmentHeaderBytes;
        return SegmentHeaderBytes;
    }

    private static BrainRecordChange ToRecordChange(StateChange change) => change switch
    {
        StateChange.Write => BrainRecordChange.Write,
        StateChange.Clear => BrainRecordChange.Clear,
        _ => BrainRecordChange.None
    };

    private static StateChange ToStateChange(BrainRecordChange change) => change switch
    {
        BrainRecordChange.Clear => StateChange.Clear,
        _ => StateChange.Write
    };

    private void Apply(BrainEntry entry)
    {
        var stateWrite = entry.StateName.Length > 0 && entry.Change != BrainRecordChange.None
            ? new RecordWrite(entry.StateName, ToStateChange(entry.Change), entry.Record, null, entry.RecordType)
            : null;
        var sideWrite = entry.SideStateName is not null
            ? new RecordWrite(entry.SideStateName, StateChange.Write, entry.SideRecord ?? [], null)
            : null;
        var jEntry = new JournalEntry<JournaledSignalPayload>(
            entry.Sequence,
            entry.GrainId,
            stateWrite,
            sideWrite,
            entry.Publisher,
            entry.Signals,
            entry.FirstSignal,
            entry.FirstPosition,
            entry.AtUnixMs);
        _journal.Apply(jEntry);
        FoldSubscriptions(jEntry);
    }

    private static void FlushToDisk(Stream stream)
    {
        if (stream is FileStream file) { file.Flush(flushToDisk: true); }
        else { stream.Flush(); }
    }

    internal static void WriteFlushed(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        FlushToDisk(stream);
    }

    // False once the ownership is lost or a write failed; BrainLogs then evicts this log so the brain can be claimed again.
    internal bool Usable => !disposed && failure is null && ownership.IsHeld;

    private void RequireOpen()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (failure is BrainNotOwnedException || (failure is null && !ownership.IsHeld)) { throw new BrainNotOwnedException(brainId, failure); }
        if (failure is not null) { throw new IOException("The brain store must be reopened after a failed append or checkpoint.", failure); }
    }

    private void TryTriggerAutoCheckpoint()
    {
        if (disposed || failure is not null || !ownership.IsHeld) { return; }
        if (uncompactedBytes < options.CheckpointBytesThreshold && uncompactedEntries < options.CheckpointEntriesThreshold) { return; }
        _ = Task.Run(async () =>
        {
            try
            {
                if (disposed || failure is not null || !ownership.IsHeld) { return; }
                await Checkpoint(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // In auto-checkpoint background runs, failures to checkpoint do not corrupt the journal;
                // if ownership was lost or I/O failed, record failure if critical.
                if (ex is BrainNotOwnedException or IOException)
                {
                    failure = ex;
                }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await checkpointGate.WaitAsync();
        try
        {
            await gate.WaitAsync();
            try
            {
                if (disposed) { return; }
                disposed = true;
            }
            finally { gate.Release(); }
        }
        finally { checkpointGate.Release(); }
        await ownership.DisposeAsync();
    }

    // A failed open cannot await in a constructor: the release starts at once and its outcome is observed, never blocked on.
    private static void ReleaseUnopened(IBrainLogLease lease)
        => _ = lease.DisposeAsync().AsTask().ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);
}
