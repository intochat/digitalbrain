using System.Buffers.Binary;
using System.Globalization;
using DigitalBrain.Runtime.Orleans.Synapses;
using Orleans.Serialization;
using Orleans.Storage;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// One brain's journal (moved from os Authority in 04-05, format <see cref="BrainLogFormat.Json"/>). One writer, proven by its
/// ownership lease. State and the signals emitted with it share one flushed, authenticated entry, so a signal is durable before
/// any observer can see it. Each publisher numbers its signals from 1; every signal also has a brain-wide position.
/// Checkpoints trim each publisher below the floor of the <see cref="IRetentionPolicy"/> (04 D-04, D-11).
/// </summary>
internal sealed class BrainLog : IAsyncDisposable
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
    // Held across each durable write and read of the journal state; async so no thread blocks behind a writer.
    private readonly SemaphoreSlim gate = new(1, 1);
    private Dictionary<string, Dictionary<string, BrainRecord>> records = new(StringComparer.Ordinal);
    // Signal-only entries still held (above each publisher's floor), in position order.
    private readonly List<BrainEntry> signals = [];
    private readonly Dictionary<string, Mark> marks = new(StringComparer.Ordinal);
    private string generation = "initial";
    private long sequence;
    private long position;
    // Read without the gate; written only under it.
    private volatile Exception? failure;
    private volatile bool disposed;

    internal BrainLog(string directory, string brainId, byte[] key, Serializer serializer, JournalPayloads payloads, TimeProvider time,
        IRetentionPolicy retention, IBrainLogLease ownership, IBrainLogFiles? files = null)
    {
        this.ownership = ownership;
        this.files = files ?? FileBrainLogFiles.Instance;
        this.directory = directory;
        this.brainId = brainId;
        this.key = key;
        this.serializer = serializer;
        this.payloads = payloads;
        this.time = time;
        this.retention = retention;
        try { Recover(); }
        catch { ReleaseUnopened(ownership); throw; }
    }

    public string BrainId => brainId;

    public JournalPayloads Payloads => payloads;

    /// <summary>The last brain-wide signal position.</summary>
    public long LastPosition => Volatile.Read(ref position);

    private static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (IOException) { return false; }
    }

    internal async Task<bool> CheckStorage(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
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

    public async Task<BrainRecord?> Read(string grainId, string stateName)
    {
        await gate.WaitAsync();
        try
        {
            RequireOpen();
            return records.TryGetValue(grainId, out var states) && states.TryGetValue(stateName, out var record)
                ? record with { Record = (byte[])record.Record.Clone() } : null;
        }
        finally { gate.Release(); }
    }

    /// <summary>Every stored record of <paramref name="stateName"/>, grain id to a copy of the bytes.</summary>
    internal List<(string Grain, byte[] Record)> Named(string stateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateName);
        gate.Wait();
        try
        {
            RequireOpen();
            var found = new List<(string, byte[])>();
            foreach (var (grain, states) in records)
            {
                if (states.TryGetValue(stateName, out var record) && record.Record.Length > 0)
                {
                    found.Add((grain, (byte[])record.Record.Clone()));
                }
            }
            return found;
        }
        finally { gate.Release(); }
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
        ArgumentNullException.ThrowIfNull(emitted);
        // Encoding runs before the gate: a payload that cannot be journaled never reaches the disk.
        JournaledSignalPayload[] encoded = [.. emitted.Select(payloads.EncodeSignal)];
        return await AppendEncoded(grainId, stateName, expectedETag, change, record, null, publisher, encoded, side, ct);
    }

    /// <summary>
    /// Appends one entry whose signals are already encoded (alias plus JSON) and whose record is opaque, optionally naming its state
    /// type: the write path of <see cref="IBrainJournalWriter"/> (04-17), in the same format as every other entry.
    /// </summary>
    internal async Task<(long Sequence, long LastSignal)> AppendEncoded(string grainId, string stateName, long? expectedETag, BrainRecordChange change,
        byte[] record, string? recordType, string publisher, JournaledSignalPayload[] encoded, BrainSideWrite? side, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (encoded.Length > 0) { ArgumentException.ThrowIfNullOrEmpty(publisher); }
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            ct.ThrowIfCancellationRequested();
            var current = records.GetValueOrDefault(grainId)?.GetValueOrDefault(stateName);
            if (change != BrainRecordChange.None && current?.ETag != expectedETag)
            {
                throw new InconsistentStateException($"State {grainId}/{stateName} changed since it was read.",
                    current?.ETag.ToString(CultureInfo.InvariantCulture) ?? "none", expectedETag?.ToString(CultureInfo.InvariantCulture) ?? "none");
            }
            if (side is not null)
            {
                if (side.StateName == stateName && change != BrainRecordChange.None) { throw new ArgumentException("The side state must be another state of the grain.", nameof(side)); }
                var stored = records.GetValueOrDefault(grainId)?.GetValueOrDefault(side.StateName)?.ETag;
                if (stored != side.ExpectedETag)
                {
                    throw new InconsistentStateException($"State {grainId}/{side.StateName} changed since it was read.",
                        stored?.ToString(CultureInfo.InvariantCulture) ?? "none", side.ExpectedETag?.ToString(CultureInfo.InvariantCulture) ?? "none");
                }
            }
            var last = encoded.Length > 0 ? marks.GetValueOrDefault(publisher).Last : 0;
            var entry = new BrainEntry(checked(sequence + 1), grainId, stateName, change, record, encoded.Length > 0 ? publisher : "", encoded,
                checked(last + 1), checked(position + 1), time.GetUtcNow().ToUnixTimeMilliseconds(), side?.StateName, side?.Record,
                change == BrainRecordChange.Write ? recordType : null);
            var plain = serializer.SerializeToArray(entry);
            if (plain.Length > MaxEntryBytes) { throw new ArgumentException("A brain change exceeds 16 MiB.", nameof(record)); }
            var bytes = SegmentSeal.Close(key, Context("log"), plain);
            var started = ownership.BeginWrite();
            try
            {
                using var stream = files.OpenAppend(Journal);
                var before = stream.Length;
                // Header and first frame go out in one write; a torn header is truncated on recovery.
                var headed = stream.Length == 0;
                var frame = new byte[(headed ? SegmentHeaderBytes : 0) + 4 + bytes.Length];
                var offset = 0;
                if (headed) { WriteHeader(frame); offset = SegmentHeaderBytes; }
                BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset), bytes.Length);
                bytes.CopyTo(frame.AsSpan(offset + 4));
                stream.Write(frame);
                // A write that outlived the lease or its time cap is cut back off the journal before it can be replayed, and is
                // not applied. Truncation is best effort; recovery also faults on a sequence that is not contiguous.
                try { ownership.EndWrite(started); }
                catch (BrainNotOwnedException)
                {
                    stream.SetLength(before);
                    FlushToDisk(stream);
                    throw;
                }
                FlushToDisk(stream);
                // Deserialize the durable bytes so callers cannot mutate the stored projection.
                Apply(serializer.Deserialize<BrainEntry>(plain)!);
            }
            catch (Exception error) { failure = error; throw; }
            return (sequence, encoded.Length > 0 ? marks[publisher].Last : marks.GetValueOrDefault(publisher).Last);
        }
        finally { gate.Release(); }
    }

    /// <summary>Held signals of <paramref name="publisher"/> after <paramref name="since"/> (publisher sequences), with its floor and last.</summary>
    public async Task<StoredSlice> PublisherSignals(string publisher, long since, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            var mark = marks.GetValueOrDefault(publisher);
            StoredSignal[] held = [.. Held().Where(item => item.Publisher == publisher && item.Sequence > since).Take(limit)];
            return new StoredSlice(mark.Floor, mark.Last, held);
        }
        finally { gate.Release(); }
    }

    /// <summary>Held signals of the whole brain after <paramref name="since"/> (brain positions).</summary>
    public async Task<StoredSlice> BrainSignals(long since, int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(since);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            var floor = signals.Count == 0 ? position : signals[0].FirstPosition - 1;
            StoredSignal[] held = [.. Held().Where(item => item.Position > since).Take(limit)];
            return new StoredSlice(floor, position, held);
        }
        finally { gate.Release(); }
    }

    // The caller holds the gate.
    private IEnumerable<StoredSignal> Held()
        => signals.SelectMany(entry => entry.Signals.Select((payload, index)
            => new StoredSignal(entry.Publisher, entry.FirstSignal + index, entry.FirstPosition + index, entry.AtUnixMs, payload)));

    /// <summary>
    /// Writes a new generation: the snapshot of every record and the held signals, after raising each publisher's floor to what the
    /// retention policy allows. The policy runs outside the gate (it may read cursors that persist in this same log).
    /// </summary>
    public async Task Checkpoint(CancellationToken ct)
    {
        var floors = await Floors(ct);
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            ct.ThrowIfCancellationRequested();
            foreach (var (publisher, floor) in floors) { Trim(publisher, floor); }
            var started = ownership.BeginWrite();
            var previous = generation;
            var next = Guid.NewGuid().ToString("N");
            var snapshot = new BrainSnapshot(sequence, position, records, [.. signals],
                [.. marks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new PublisherMark(pair.Key, pair.Value.Floor, pair.Value.Last))]);
            var body = serializer.SerializeToArray(snapshot);
            var plain = new byte[SegmentHeaderBytes + body.Length];
            WriteHeader(plain);
            body.CopyTo(plain.AsSpan(SegmentHeaderBytes));
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
            generation = next;
            // Old complete generation remains until the manifest points to the new one.
            foreach (var suffix in new[] { ".snapshot", ".log" })
            {
                // Unreachable generations may be removed on a later maintenance run.
                TryDelete(Path.Combine(directory, previous + suffix));
            }
        }
        finally { gate.Release(); }
    }

    private async Task<List<(string Publisher, long Floor)>> Floors(CancellationToken ct)
    {
        List<(string Publisher, NeuronId Id, long Last, long[] At)> inputs = [];
        await gate.WaitAsync(ct);
        try
        {
            RequireOpen();
            var held = Held().GroupBy(item => item.Publisher, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(item => item.AtUnixMs).ToArray(), StringComparer.Ordinal);
            foreach (var (publisher, mark) in marks)
            {
                // Only neuron publishers have synapses and cursors; anything else keeps its whole history.
                if (mark.Last <= mark.Floor || !NeuronId.TryParse(publisher, out var id)) { continue; }
                inputs.Add((publisher, id, mark.Last, held.GetValueOrDefault(publisher) ?? []));
            }
        }
        finally { gate.Release(); }
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
        if (!marks.TryGetValue(publisher, out var mark)) { return; }
        floor = Math.Min(floor, mark.Last);
        if (floor <= mark.Floor) { return; }
        marks[publisher] = mark with { Floor = floor };
        for (var index = signals.Count - 1; index >= 0; index--)
        {
            var entry = signals[index];
            if (entry.Publisher != publisher || entry.FirstSignal > floor) { continue; }
            var drop = (int)Math.Min(entry.Signals.Length, floor - entry.FirstSignal + 1);
            if (drop == entry.Signals.Length) { signals.RemoveAt(index); continue; }
            signals[index] = entry with { Signals = entry.Signals[drop..], FirstSignal = entry.FirstSignal + drop, FirstPosition = entry.FirstPosition + drop };
        }
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
            if (entry.Sequence != sequence + 1 || entry.FirstPosition != position + 1
                || (entry.Signals.Length > 0 && entry.FirstSignal != marks.GetValueOrDefault(entry.Publisher).Last + 1))
            { throw new InvalidDataException("The local brain log is not contiguous."); }
            Apply(entry);
            valid = stream.Position;
        }
        if (valid != stream.Length) { stream.SetLength(valid); FlushToDisk(stream); }
    }

    // Held signals of each publisher run contiguously from Floor + 1 to Last; positions rise strictly.
    private void RestoreSnapshot(BrainSnapshot checkpoint)
    {
        records = checkpoint.Records;
        sequence = checkpoint.Sequence;
        position = checkpoint.Position;
        foreach (var mark in checkpoint.Publishers)
        {
            if (mark.Floor < 0 || mark.Floor > mark.Last) { throw new InvalidDataException("A snapshot publisher floor is out of range."); }
            marks[mark.Publisher] = new Mark(mark.Floor, mark.Last);
        }
        var next = marks.ToDictionary(pair => pair.Key, pair => pair.Value.Floor + 1, StringComparer.Ordinal);
        long lastPosition = 0;
        foreach (var entry in checkpoint.Signals)
        {
            if (!next.TryGetValue(entry.Publisher, out var expected) || entry.FirstSignal != expected || entry.FirstPosition <= lastPosition)
            { throw new InvalidDataException("The brain snapshot signals are not contiguous above their floor."); }
            next[entry.Publisher] = expected + entry.Signals.Length;
            lastPosition = entry.FirstPosition + entry.Signals.Length - 1;
            signals.Add(entry);
        }
        if (lastPosition > position || next.Any(pair => pair.Value != marks[pair.Key].Last + 1))
        { throw new InvalidDataException("The brain snapshot signals do not reach their publishers' last sequence."); }
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

    private void Apply(BrainEntry entry)
    {
        if (entry.Change == BrainRecordChange.Write)
        {
            if (!records.TryGetValue(entry.GrainId, out var states)) { records[entry.GrainId] = states = new(StringComparer.Ordinal); }
            states[entry.StateName] = new BrainRecord(entry.Record, entry.Sequence, entry.RecordType);
        }
        else if (entry.Change == BrainRecordChange.Clear) { records.GetValueOrDefault(entry.GrainId)?.Remove(entry.StateName); }
        if (entry.SideStateName is { } side && entry.SideRecord is { } sideRecord)
        {
            if (!records.TryGetValue(entry.GrainId, out var held)) { records[entry.GrainId] = held = new(StringComparer.Ordinal); }
            held[side] = new BrainRecord(sideRecord, entry.Sequence);
        }
        if (entry.Signals.Length > 0)
        {
            signals.Add(entry with { Record = [], StateName = "", GrainId = "", Change = BrainRecordChange.None, SideStateName = null, SideRecord = null, RecordType = null });
            marks[entry.Publisher] = marks.GetValueOrDefault(entry.Publisher) with { Last = entry.FirstSignal + entry.Signals.Length - 1 };
            position = entry.FirstPosition + entry.Signals.Length - 1;
        }
        sequence = entry.Sequence;
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

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) { return; }
            disposed = true;
        }
        finally { gate.Release(); }
        await ownership.DisposeAsync();
    }

    // A failed open cannot await in a constructor: the release starts at once and its outcome is observed, never blocked on.
    private static void ReleaseUnopened(IBrainLogLease lease)
        => _ = lease.DisposeAsync().AsTask().ContinueWith(static task => _ = task.Exception, TaskScheduler.Default);

    private readonly record struct Mark(long Floor, long Last);
}
