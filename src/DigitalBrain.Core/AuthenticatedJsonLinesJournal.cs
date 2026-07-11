using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Core.Runtime;

internal sealed record AuthenticatedJournalRecord(
    int LineNumber,
    string? Kind,
    string Payload,
    bool IsLegacy,
    string SourceDigest,
    int ByteLength);

internal sealed record AuthenticatedJournalFaultInjection(
    Action? BeforePhysicalAppend = null,
    Action? BeforeHeadWrite = null);

internal sealed class AuthenticatedJsonLinesJournal
{
    private const int FormatVersion = 1;
    private const string FormatMarker = "digitalbrain.authenticated-jsonl.v1";
    private const string SealKind = "journal.seal";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _domain;
    private readonly byte[] _integrityKey;
    private readonly string _path;
    private readonly string _headPath;
    private readonly string _pendingPath;
    private readonly AuthenticatedJournalFaultInjection? _faultInjection;
    private readonly object _gate = new();
    private bool _loaded;
    private bool _legacyPending;
    private bool _poisoned;
    private long _sequence;
    private string _headDigest;

    public AuthenticatedJsonLinesJournal(
        string domain,
        byte[] integrityKey,
        string path,
        AuthenticatedJournalFaultInjection? faultInjection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(integrityKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (integrityKey.Length < 32)
            throw new ArgumentException("Journal integrity keys must be at least 256 bits.", nameof(integrityKey));

        _domain = domain;
        _integrityKey = integrityKey.ToArray();
        _path = Path.GetFullPath(path);
        _headPath = _path + ".head";
        _pendingPath = _path + ".pending";
        _faultInjection = faultInjection;
        _headDigest = GenesisDigest(domain);
    }

    public IReadOnlyList<AuthenticatedJournalRecord> Read()
    {
        lock (_gate)
        {
            EnsureHealthy();
            if (_loaded) throw new InvalidOperationException("The authenticated journal has already been read.");
            using var exclusion = JsonLinesJournalFile.AcquireWriterExclusion(_path);
            var snapshot = InspectAndRecoverUnderExclusion();
            Apply(snapshot);
            _loaded = true;
            return snapshot.Records;
        }
    }

    public void SealLegacy()
    {
        lock (_gate)
        {
            EnsureReady();
            using var exclusion = JsonLinesJournalFile.AcquireWriterExclusion(_path);
            var cachedLegacySequence = _sequence;
            var cachedLegacyDigest = _headDigest;
            var snapshot = InspectAndRecoverUnderExclusion();
            if (!snapshot.LegacyPending)
            {
                Apply(snapshot);
                return;
            }
            if (!_legacyPending || snapshot.Sequence != cachedLegacySequence ||
                !FixedTimeHexEquals(snapshot.HeadDigest, cachedLegacyDigest))
                throw InvalidJournal("The plaintext journal changed before its authenticated migration seal could be appended.");

            Apply(snapshot);
            var payload = JsonSerializer.SerializeToElement(new { legacyLineCount = _sequence }, JsonOptions);
            AppendEnvelopeUnderExclusion(SealKind, payload);
            _legacyPending = false;
        }
    }

    public void Append(string kind, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        if (string.Equals(kind, SealKind, StringComparison.Ordinal))
            throw new ArgumentException("The reserved journal seal kind cannot be appended directly.", nameof(kind));
        JsonElement parsed;
        try
        {
            using var document = JsonDocument.Parse(payload);
            parsed = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The authenticated journal payload must be valid JSON.", nameof(payload), exception);
        }

        lock (_gate)
        {
            EnsureReady();
            using var exclusion = JsonLinesJournalFile.AcquireWriterExclusion(_path);
            var snapshot = InspectAndRecoverUnderExclusion();
            if (snapshot.LegacyPending)
                throw new InvalidOperationException("The plaintext journal prefix must be sealed before authenticated records are appended.");
            Apply(snapshot);
            AppendEnvelopeUnderExclusion(kind, parsed);
        }
    }

    public InvalidDataException Invalid(AuthenticatedJournalRecord record, string reason, string message)
    {
        JsonLinesJournalFile.QuarantineDigest(_path, record.LineNumber, record.SourceDigest, record.ByteLength, reason);
        return new InvalidDataException(message);
    }

    private JournalSnapshot InspectAndRecoverUnderExclusion()
    {
        var anchor = ReadAnchor();
        var pending = ReadPending();
        var inspection = JsonLinesJournalFile.Inspect(_path);
        var snapshot = ParseSnapshot(inspection);
        if (pending is not null)
            return RecoverPendingAppend(anchor, pending, inspection, snapshot);

        if (anchor is null)
        {
            if (snapshot.HasAuthenticatedRecords)
                throw InvalidJournal("The authenticated journal head is missing; automatic downgrade to plaintext is forbidden.");
            if (inspection.IncompleteTail is not null)
                throw InvalidJournal("An unsealed plaintext journal has an incomplete final record and cannot be repaired without authenticated authority.");
            return snapshot;
        }

        var anchorPoint = RequireAnchorPoint(anchor, snapshot);
        if (snapshot.Sequence != anchor.Sequence || !FixedTimeHexEquals(snapshot.HeadDigest, anchor.Digest))
            throw InvalidJournal(snapshot.Sequence > anchor.Sequence
                ? "The authenticated journal contains an unanchored suffix."
                : "The authenticated journal was rolled back behind its durable head.");

        if (inspection.IncompleteTail is not null)
        {
            if (inspection.IncompleteTail.StartOffset != anchorPoint.EndOffset)
                throw InvalidJournal("The authenticated journal head itself was truncated; repair is forbidden.");
            JsonLinesJournalFile.QuarantineBytes(
                _path,
                inspection.IncompleteTail.LineNumber,
                inspection.IncompleteTail.Bytes,
                "incomplete-final-append");
            JsonLinesJournalFile.TruncateVerifiedTail(_path, inspection.IncompleteTail);
        }
        return snapshot;
    }

    private JournalSnapshot RecoverPendingAppend(
        JournalAnchor? anchor,
        JournalPending pending,
        JsonLinesInspection inspection,
        JournalSnapshot snapshot)
    {
        if (pending.NextSequence != checked(pending.PreviousSequence + 1))
            throw InvalidJournal("The authenticated pending append has an invalid sequence.");
        var previousPoint = ResolvePendingPreviousPoint(anchor, pending, snapshot);
        if (previousPoint.EndOffset != pending.JournalLengthBeforeAppend)
            throw InvalidJournal("The authenticated pending append does not bind the previous journal length.");
        if (inspection.FileLength < pending.JournalLengthBeforeAppend)
            throw InvalidJournal("The journal was rolled back behind an authenticated pending append.");

        if (inspection.FileLength == pending.JournalLengthBeforeAppend)
        {
            RequireSnapshotAtPrevious(snapshot, pending);
            DeletePendingRequired();
            return snapshot;
        }

        if (inspection.IncompleteTail is not null)
        {
            if (inspection.IncompleteTail.StartOffset != pending.JournalLengthBeforeAppend)
                throw InvalidJournal("The pending append is followed by an unexpected or truncated authenticated record.");
            RequireSnapshotAtPrevious(snapshot, pending);
            JsonLinesJournalFile.QuarantineBytes(
                _path,
                inspection.IncompleteTail.LineNumber,
                inspection.IncompleteTail.Bytes,
                "incomplete-pending-append");
            JsonLinesJournalFile.TruncateVerifiedTail(_path, inspection.IncompleteTail);
            DeletePendingRequired();
            return ParseSnapshot(JsonLinesJournalFile.Inspect(_path));
        }

        if (snapshot.Sequence != pending.NextSequence || !FixedTimeHexEquals(snapshot.HeadDigest, pending.NextDigest) ||
            !snapshot.AuthenticatedHeads.TryGetValue(pending.NextSequence, out var nextPoint) ||
            nextPoint.EndOffset != inspection.FileLength)
            throw InvalidJournal("The authenticated pending append does not match the journal tail.");

        var appendedLength = inspection.FileLength - pending.JournalLengthBeforeAppend;
        if (appendedLength != pending.AppendedByteLength)
        {
            if (!inspection.HasUnterminatedCompleteLine || appendedLength + 1 != pending.AppendedByteLength)
                throw InvalidJournal("The authenticated pending append has an unexpected physical length.");
            JsonLinesJournalFile.AppendVerifiedNewline(_path, inspection.FileLength);
            inspection = JsonLinesJournalFile.Inspect(_path);
            snapshot = ParseSnapshot(inspection);
        }

        if (anchor is not null &&
            !((anchor.Sequence == pending.PreviousSequence && FixedTimeHexEquals(anchor.Digest, pending.PreviousDigest)) ||
              (anchor.Sequence == pending.NextSequence && FixedTimeHexEquals(anchor.Digest, pending.NextDigest))))
            throw InvalidJournal("The durable head conflicts with the authenticated pending append.");
        if (anchor is null || anchor.Sequence != pending.NextSequence || !FixedTimeHexEquals(anchor.Digest, pending.NextDigest))
            WriteAnchor(pending.NextSequence, pending.NextDigest, applyFaultInjection: false);
        DeletePendingRequired();
        return snapshot;
    }

    private JournalHeadPoint ResolvePendingPreviousPoint(
        JournalAnchor? anchor,
        JournalPending pending,
        JournalSnapshot snapshot)
    {
        JournalHeadPoint previousPoint;
        if (pending.PreviousSequence == 0)
        {
            previousPoint = new(GenesisDigest(_domain), 0);
        }
        else if (snapshot.AuthenticatedHeads.TryGetValue(pending.PreviousSequence, out var authenticatedPoint))
        {
            previousPoint = authenticatedPoint;
        }
        else if (snapshot.LegacySequence == pending.PreviousSequence)
        {
            previousPoint = new(snapshot.LegacyDigest, snapshot.LegacyEndOffset);
        }
        else
        {
            throw InvalidJournal("The journal does not contain the state bound by its authenticated pending append.");
        }

        if (!FixedTimeHexEquals(previousPoint.Digest, pending.PreviousDigest))
            throw InvalidJournal("The authenticated pending append does not match the previous journal digest.");
        if (anchor is null) return previousPoint;
        var anchorMatchesPrevious = anchor.Sequence == pending.PreviousSequence &&
                                    FixedTimeHexEquals(anchor.Digest, pending.PreviousDigest);
        var anchorMatchesNext = anchor.Sequence == pending.NextSequence &&
                                FixedTimeHexEquals(anchor.Digest, pending.NextDigest);
        if (!anchorMatchesPrevious && !anchorMatchesNext)
            throw InvalidJournal("The durable head conflicts with the authenticated pending append.");
        if (anchorMatchesPrevious)
        {
            var anchorPoint = RequireAnchorPoint(anchor, snapshot);
            if (anchorPoint.EndOffset != previousPoint.EndOffset)
                throw InvalidJournal("The durable head length conflicts with the authenticated pending append.");
        }
        return previousPoint;
    }

    private static void RequireSnapshotAtPrevious(JournalSnapshot snapshot, JournalPending pending)
    {
        if (snapshot.Sequence != pending.PreviousSequence ||
            !FixedTimeHexEquals(snapshot.HeadDigest, pending.PreviousDigest))
            throw InvalidJournal("The journal changed before the pending append could be recovered.");
    }

    private void AppendEnvelopeUnderExclusion(string kind, JsonElement payload)
    {
        JsonLinesJournalFile.EnsureTerminatingNewline(_path);
        var previousLength = File.Exists(_path) ? new FileInfo(_path).Length : 0;
        var nextSequence = checked(_sequence + 1);
        var body = new JournalBody(FormatVersion, _domain, nextSequence, kind, _headDigest, payload);
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        var digest = Convert.ToHexString(SHA256.HashData(bodyBytes));
        var authenticationCode = Convert.ToHexString(HMACSHA256.HashData(_integrityKey, bodyBytes));
        var envelope = new JournalEnvelope(
            FormatMarker,
            body.Version,
            body.Domain,
            body.Sequence,
            body.Kind,
            body.PreviousDigest,
            body.Payload,
            digest,
            authenticationCode);
        var lineBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOptions) + "\n");
        WritePending(new JournalPendingBody(
            FormatVersion,
            _domain,
            _sequence,
            _headDigest,
            nextSequence,
            digest,
            previousLength,
            lineBytes.Length));

        try
        {
            _faultInjection?.BeforePhysicalAppend?.Invoke();
        }
        catch
        {
            DeletePendingRequired();
            throw;
        }

        try
        {
            AppendPhysicalBytes(lineBytes, previousLength);
            _sequence = nextSequence;
            _headDigest = digest;
            WriteAnchor(_sequence, _headDigest, applyFaultInjection: true);
        }
        catch (Exception exception)
        {
            _poisoned = true;
            throw new InvalidDataException(
                "The journal append may have reached durable storage but its head was not confirmed. Reopen the store before continuing.",
                exception);
        }
        if (!TryDeletePending())
        {
            _poisoned = true;
            throw new InvalidDataException(
                "The journal head was committed but its pending witness could not be cleared. Reopen the store before continuing.");
        }
    }

    private JournalSnapshot ParseSnapshot(JsonLinesInspection inspection)
    {
        var sequence = 0L;
        var headDigest = GenesisDigest(_domain);
        var markerIndex = inspection.Lines.FindIndex(static line => HasFormatMarker(line.Text));
        var legacyCount = markerIndex < 0 ? inspection.Lines.Count : markerIndex;
        var records = new List<AuthenticatedJournalRecord>(inspection.Lines.Count);
        for (var index = 0; index < legacyCount; index++)
        {
            var line = inspection.Lines[index];
            AdvanceLegacy(ref sequence, ref headDigest, line);
            if (!string.IsNullOrWhiteSpace(line.Text)) records.Add(ToLegacyRecord(line));
        }
        var legacySequence = sequence;
        var legacyDigest = headDigest;
        var legacyEndOffset = legacyCount == 0 ? 0 : inspection.Lines[legacyCount - 1].EndOffset;
        if (markerIndex < 0)
            return new(records, inspection.Lines.Count > 0, false, sequence, headDigest,
                legacySequence, legacyDigest, legacyEndOffset, new Dictionary<long, JournalHeadPoint>());

        var heads = new Dictionary<long, JournalHeadPoint>();
        for (var index = markerIndex; index < inspection.Lines.Count; index++)
        {
            var line = inspection.Lines[index];
            var envelope = ParseAndVerify(line, sequence + 1, headDigest);
            var firstAuthenticatedRecord = index == markerIndex;
            if (firstAuthenticatedRecord && markerIndex > 0 && !string.Equals(envelope.Kind, SealKind, StringComparison.Ordinal))
                throw Invalid(line, "missing-legacy-seal", "The plaintext journal prefix is not followed by its authenticated migration seal.");
            if (!firstAuthenticatedRecord && string.Equals(envelope.Kind, SealKind, StringComparison.Ordinal))
                throw Invalid(line, "unexpected-seal", "The authenticated journal contains an unexpected interior seal.");
            sequence = envelope.Sequence;
            headDigest = envelope.Digest;
            heads[sequence] = new(headDigest, line.EndOffset);
            if (!string.Equals(envelope.Kind, SealKind, StringComparison.Ordinal))
                records.Add(new AuthenticatedJournalRecord(
                    line.LineNumber,
                    envelope.Kind,
                    envelope.Payload!.Value.GetRawText(),
                    false,
                    line.Sha256,
                    line.ByteLength));
        }
        return new(records, false, true, sequence, headDigest,
            legacySequence, legacyDigest, legacyEndOffset, heads);
    }

    private JournalEnvelope ParseAndVerify(JsonJournalLine line, long expectedSequence, string expectedPreviousDigest)
    {
        JournalEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<JournalEnvelope>(line.Text, JsonOptions);
        }
        catch (JsonException)
        {
            throw Invalid(line, "invalid-envelope", "The authenticated journal contains a malformed envelope.");
        }
        if (envelope is null || !string.Equals(envelope.Marker, FormatMarker, StringComparison.Ordinal) ||
            envelope.Version != FormatVersion || !string.Equals(envelope.Domain, _domain, StringComparison.Ordinal) ||
            envelope.Sequence != expectedSequence || string.IsNullOrWhiteSpace(envelope.Kind) || envelope.Kind.Length > 128 ||
            envelope.Payload is null || !IsDigest(envelope.PreviousDigest) || !IsDigest(envelope.Digest) ||
            !IsDigest(envelope.AuthenticationCode) || !FixedTimeHexEquals(envelope.PreviousDigest, expectedPreviousDigest))
            throw Invalid(line, "invalid-envelope", "The authenticated journal contains an invalid or out-of-order envelope.");

        var body = new JournalBody(
            envelope.Version,
            envelope.Domain,
            envelope.Sequence,
            envelope.Kind,
            envelope.PreviousDigest,
            envelope.Payload.Value);
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
        var expectedDigest = Convert.ToHexString(SHA256.HashData(bodyBytes));
        var expectedAuthenticationCode = Convert.ToHexString(HMACSHA256.HashData(_integrityKey, bodyBytes));
        if (!FixedTimeHexEquals(envelope.Digest, expectedDigest) ||
            !FixedTimeHexEquals(envelope.AuthenticationCode, expectedAuthenticationCode))
            throw Invalid(line, "authentication-failed", "The authenticated journal contains a record with invalid integrity metadata.");
        return envelope;
    }

    private JournalHeadPoint RequireAnchorPoint(JournalAnchor anchor, JournalSnapshot snapshot)
    {
        if (!snapshot.AuthenticatedHeads.TryGetValue(anchor.Sequence, out var point) ||
            !FixedTimeHexEquals(anchor.Digest, point.Digest))
            throw InvalidJournal("The authenticated journal does not contain its durable head.");
        return point;
    }

    private JournalAnchor? ReadAnchor()
    {
        if (!File.Exists(_headPath)) return null;
        var bytes = File.ReadAllBytes(_headPath);
        if (bytes.Length == 0 || bytes.Length > 4096) throw InvalidSidecar(_headPath, bytes, "invalid-head");
        JournalAnchor? anchor;
        try { anchor = JsonSerializer.Deserialize<JournalAnchor>(bytes, JsonOptions); }
        catch (JsonException) { throw InvalidSidecar(_headPath, bytes, "invalid-head"); }
        if (anchor is null || anchor.Version != FormatVersion || !string.Equals(anchor.Domain, _domain, StringComparison.Ordinal) ||
            anchor.Sequence <= 0 || !IsDigest(anchor.Digest) || !IsDigest(anchor.AuthenticationCode))
            throw InvalidSidecar(_headPath, bytes, "invalid-head");
        var body = new JournalAnchorBody(anchor.Version, anchor.Domain, anchor.Sequence, anchor.Digest);
        var expected = Authenticate(body);
        if (!FixedTimeHexEquals(anchor.AuthenticationCode, expected))
            throw InvalidSidecar(_headPath, bytes, "head-authentication-failed");
        return anchor;
    }

    private JournalPending? ReadPending()
    {
        if (!File.Exists(_pendingPath)) return null;
        var bytes = File.ReadAllBytes(_pendingPath);
        if (bytes.Length == 0 || bytes.Length > 8192) throw InvalidSidecar(_pendingPath, bytes, "invalid-pending");
        JournalPending? pending;
        try { pending = JsonSerializer.Deserialize<JournalPending>(bytes, JsonOptions); }
        catch (JsonException) { throw InvalidSidecar(_pendingPath, bytes, "invalid-pending"); }
        if (pending is null || pending.Version != FormatVersion || !string.Equals(pending.Domain, _domain, StringComparison.Ordinal) ||
            pending.PreviousSequence < 0 || pending.NextSequence <= 0 || !IsDigest(pending.PreviousDigest) ||
            !IsDigest(pending.NextDigest) || pending.JournalLengthBeforeAppend < 0 || pending.AppendedByteLength <= 0 ||
            pending.AppendedByteLength > 64 * 1024 * 1024 || !IsDigest(pending.AuthenticationCode))
            throw InvalidSidecar(_pendingPath, bytes, "invalid-pending");
        var body = new JournalPendingBody(
            pending.Version,
            pending.Domain,
            pending.PreviousSequence,
            pending.PreviousDigest,
            pending.NextSequence,
            pending.NextDigest,
            pending.JournalLengthBeforeAppend,
            pending.AppendedByteLength);
        if (!FixedTimeHexEquals(pending.AuthenticationCode, Authenticate(body)))
            throw InvalidSidecar(_pendingPath, bytes, "pending-authentication-failed");
        return pending;
    }

    private void WriteAnchor(long sequence, string digest, bool applyFaultInjection)
    {
        if (applyFaultInjection) _faultInjection?.BeforeHeadWrite?.Invoke();
        var body = new JournalAnchorBody(FormatVersion, _domain, sequence, digest);
        WriteAtomic(_headPath, JsonSerializer.SerializeToUtf8Bytes(
            new JournalAnchor(body.Version, body.Domain, body.Sequence, body.Digest, Authenticate(body)), JsonOptions));
    }

    private void WritePending(JournalPendingBody body) =>
        WriteAtomic(_pendingPath, JsonSerializer.SerializeToUtf8Bytes(
            new JournalPending(
                body.Version,
                body.Domain,
                body.PreviousSequence,
                body.PreviousDigest,
                body.NextSequence,
                body.NextDigest,
                body.JournalLengthBeforeAppend,
                body.AppendedByteLength,
                Authenticate(body)), JsonOptions));

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void AppendPhysicalBytes(byte[] bytes, long expectedLength)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var stream = new FileStream(_path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length != expectedLength)
            throw new InvalidDataException("The journal changed while an authenticated append was being committed.");
        stream.Position = stream.Length;
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void DeletePendingRequired()
    {
        if (!TryDeletePending())
        {
            _poisoned = true;
            throw new InvalidDataException("The authenticated pending append could not be cleared; reopen the store before continuing.");
        }
    }

    private bool TryDeletePending()
    {
        try
        {
            if (File.Exists(_pendingPath)) File.Delete(_pendingPath);
            return !File.Exists(_pendingPath);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void Apply(JournalSnapshot snapshot)
    {
        _sequence = snapshot.Sequence;
        _headDigest = snapshot.HeadDigest;
        _legacyPending = snapshot.LegacyPending;
    }

    private string Authenticate<T>(T body) =>
        Convert.ToHexString(HMACSHA256.HashData(_integrityKey, JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions)));

    private InvalidDataException Invalid(JsonJournalLine line, string reason, string message)
    {
        JsonLinesJournalFile.QuarantineDigest(_path, line.LineNumber, line.Sha256, line.ByteLength, reason);
        return new InvalidDataException(message);
    }

    private InvalidDataException InvalidSidecar(string path, byte[] bytes, string reason)
    {
        JsonLinesJournalFile.QuarantineBytes(path, 1, bytes, reason);
        return InvalidJournal("The authenticated journal sidecar is invalid.");
    }

    private static InvalidDataException InvalidJournal(string message) => new(message);

    private static void AdvanceLegacy(ref long sequence, ref string headDigest, JsonJournalLine line)
    {
        sequence = checked(sequence + 1);
        var lineBytes = Encoding.UTF8.GetBytes(line.Text);
        var prefix = Encoding.UTF8.GetBytes(headDigest + "\n" + lineBytes.Length + "\n");
        var chained = new byte[prefix.Length + lineBytes.Length];
        prefix.CopyTo(chained, 0);
        lineBytes.CopyTo(chained, prefix.Length);
        headDigest = Convert.ToHexString(SHA256.HashData(chained));
    }

    private static AuthenticatedJournalRecord ToLegacyRecord(JsonJournalLine line) =>
        new(line.LineNumber, null, line.Text, true, line.Sha256, line.ByteLength);

    private static bool HasFormatMarker(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("$journal", out _);
        }
        catch (JsonException) { return false; }
    }

    private static string GenesisDigest(string domain) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("digitalbrain-journal-genesis\n" + domain)));

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(static character => Uri.IsHexDigit(character));

    private static bool FixedTimeHexEquals(string first, string second)
    {
        if (!IsDigest(first) || !IsDigest(second)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));
    }

    private void EnsureReady()
    {
        EnsureHealthy();
        if (!_loaded) throw new InvalidOperationException("The authenticated journal must be read before it is changed.");
    }

    private void EnsureHealthy()
    {
        if (_poisoned)
            throw new InvalidOperationException("The authenticated journal is poisoned after an ambiguous append. Reopen the store.");
    }

    private sealed record JournalSnapshot(
        IReadOnlyList<AuthenticatedJournalRecord> Records,
        bool LegacyPending,
        bool HasAuthenticatedRecords,
        long Sequence,
        string HeadDigest,
        long LegacySequence,
        string LegacyDigest,
        long LegacyEndOffset,
        IReadOnlyDictionary<long, JournalHeadPoint> AuthenticatedHeads);

    private sealed record JournalHeadPoint(string Digest, long EndOffset);
    private sealed record JournalBody(int Version, string Domain, long Sequence, string Kind, string PreviousDigest, JsonElement Payload);
    private sealed record JournalEnvelope(
        [property: JsonPropertyName("$journal")] string Marker,
        int Version,
        string Domain,
        long Sequence,
        string Kind,
        string PreviousDigest,
        JsonElement? Payload,
        string Digest,
        string AuthenticationCode);
    private sealed record JournalAnchorBody(int Version, string Domain, long Sequence, string Digest);
    private sealed record JournalAnchor(int Version, string Domain, long Sequence, string Digest, string AuthenticationCode);
    private sealed record JournalPendingBody(
        int Version,
        string Domain,
        long PreviousSequence,
        string PreviousDigest,
        long NextSequence,
        string NextDigest,
        long JournalLengthBeforeAppend,
        long AppendedByteLength);
    private sealed record JournalPending(
        int Version,
        string Domain,
        long PreviousSequence,
        string PreviousDigest,
        long NextSequence,
        string NextDigest,
        long JournalLengthBeforeAppend,
        long AppendedByteLength,
        string AuthenticationCode);
}

internal sealed record JsonJournalLine(
    int LineNumber,
    string Text,
    string Sha256,
    int ByteLength,
    long StartOffset,
    long EndOffset);

internal sealed record IncompleteJsonTail(int LineNumber, long StartOffset, byte[] Bytes, string Sha256);
internal sealed record JsonLinesInspection(
    List<JsonJournalLine> Lines,
    long FileLength,
    bool EndsWithNewline,
    bool HasUnterminatedCompleteLine,
    IncompleteJsonTail? IncompleteTail);
internal sealed record JsonLinesReadResult(List<JsonJournalLine> Lines, bool EndsWithNewline);

internal static class JsonLinesJournalFile
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static JsonLinesInspection Inspect(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return new([], 0, true, false, null);
        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length == 0) return new([], 0, true, false, null);
        var endsWithNewline = bytes[^1] == (byte)'\n';
        var parseLength = bytes.Length;
        var hasUnterminatedCompleteLine = false;
        IncompleteJsonTail? incompleteTail = null;
        if (!endsWithNewline)
        {
            var lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
            var tailStart = lastNewline + 1;
            var tail = bytes.AsMemory(tailStart);
            if (IsCompleteJson(tail))
            {
                hasUnterminatedCompleteLine = true;
            }
            else
            {
                var lineNumber = CountNewlines(bytes.AsSpan(0, tailStart)) + 1;
                incompleteTail = new IncompleteJsonTail(
                    lineNumber,
                    tailStart,
                    tail.ToArray(),
                    Convert.ToHexString(SHA256.HashData(tail.Span)));
                parseLength = tailStart;
            }
        }

        var lines = new List<JsonJournalLine>();
        var start = 0;
        var lineNumberValue = 1;
        for (var index = 0; index < parseLength; index++)
        {
            if (bytes[index] != (byte)'\n') continue;
            AddLine(bytes.AsSpan(start, index - start), lineNumberValue++, start, index + 1, lines, fullPath);
            start = index + 1;
        }
        if (start < parseLength)
            AddLine(bytes.AsSpan(start, parseLength - start), lineNumberValue, start, parseLength, lines, fullPath);
        return new(lines, bytes.Length, endsWithNewline, hasUnterminatedCompleteLine, incompleteTail);
    }

    // Compatibility path for the independently authenticated feed journal. The chained
    // session/operation journal never calls this eager repair helper.
    public static JsonLinesReadResult ReadCompleteLines(string path)
    {
        using var exclusion = AcquireWriterExclusion(path);
        var inspection = Inspect(path);
        if (inspection.IncompleteTail is null)
            return new(inspection.Lines, inspection.EndsWithNewline);
        QuarantineBytes(path, inspection.IncompleteTail.LineNumber, inspection.IncompleteTail.Bytes, "incomplete-final-append");
        TruncateVerifiedTail(path, inspection.IncompleteTail);
        return new(inspection.Lines, true);
    }

    public static FileStream AcquireWriterExclusion(string path)
    {
        var lockPath = Path.GetFullPath(path) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var stopwatch = Stopwatch.StartNew();
        IOException? lastError = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(10);
            }
        }
        throw new IOException("Timed out waiting for exclusive access to the JSON-lines journal.", lastError);
    }

    public static void TruncateVerifiedTail(string path, IncompleteJsonTail tail)
    {
        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length != tail.StartOffset + tail.Bytes.LongLength)
            throw new InvalidDataException("The journal changed before its incomplete tail could be repaired.");
        stream.Position = tail.StartOffset;
        var current = new byte[tail.Bytes.Length];
        stream.ReadExactly(current);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(current), Convert.FromHexString(tail.Sha256)))
            throw new InvalidDataException("The journal tail changed before repair.");
        stream.SetLength(tail.StartOffset);
        stream.Flush(flushToDisk: true);
    }

    public static void AppendVerifiedNewline(string path, long expectedLength)
    {
        using var stream = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length != expectedLength)
            throw new InvalidDataException("The journal changed before its delimiter could be repaired.");
        stream.Position = stream.Length;
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    public static void EnsureTerminatingNewline(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return;
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length == 0) return;
        stream.Position = stream.Length - 1;
        if (stream.ReadByte() == (byte)'\n') return;
        stream.Position = stream.Length;
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    public static void QuarantineBytes(string path, int lineNumber, ReadOnlySpan<byte> raw, string reason)
    {
        var digest = Convert.ToHexString(SHA256.HashData(raw));
        QuarantineDigest(path, lineNumber, digest, raw.Length, reason);
    }

    public static void QuarantineDigest(string path, int lineNumber, string digest, int byteLength, string reason)
    {
        try
        {
            var quarantinePath = Path.GetFullPath(path) + ".quarantine";
            Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)!);
            var boundedReason = new string(reason.Where(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_').Take(64).ToArray());
            var entry = JsonSerializer.Serialize(new
            {
                line = lineNumber,
                sha256 = digest,
                bytes = Math.Clamp(byteLength, 0, 16 * 1024 * 1024),
                reason = boundedReason
            });
            File.AppendAllText(quarantinePath, entry + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void AddLine(
        ReadOnlySpan<byte> raw,
        int lineNumber,
        long startOffset,
        long endOffset,
        List<JsonJournalLine> lines,
        string path)
    {
        if (raw.Length > 0 && raw[^1] == (byte)'\r') raw = raw[..^1];
        string text;
        try { text = StrictUtf8.GetString(raw); }
        catch (DecoderFallbackException)
        {
            QuarantineBytes(path, lineNumber, raw, "invalid-utf8");
            throw new InvalidDataException($"The JSON-lines journal contains invalid UTF-8 at line {lineNumber}.");
        }
        lines.Add(new JsonJournalLine(
            lineNumber,
            text,
            Convert.ToHexString(SHA256.HashData(raw)),
            raw.Length,
            startOffset,
            endOffset));
    }

    private static bool IsCompleteJson(ReadOnlyMemory<byte> value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind != JsonValueKind.Undefined;
        }
        catch (JsonException) { return false; }
    }

    private static int CountNewlines(ReadOnlySpan<byte> value)
    {
        var count = 0;
        foreach (var item in value)
            if (item == (byte)'\n') count++;
        return count;
    }
}
