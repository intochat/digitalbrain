using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.RuntimeMigration;

public sealed class ReadOnlyAuthenticatedJournalReader(string domain, byte[] integrityKey, string path) : IDisposable
{
    private const int FormatVersion = 1;
    private const string FormatMarker = "digitalbrain.authenticated-jsonl.v1";
    private const string SealKind = "journal.seal";
    private const long MaximumJournalBytes = 1024L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _integrityKey = integrityKey is { Length: >= 32 }
        ? integrityKey.ToArray()
        : throw new MigrationGapException("legacy-key-invalid");
    private bool _disposed;

    public VerifiedJournal Read()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ReadOnlyAuthenticatedJournalReader));
        if (File.Exists(path + ".pending")) throw new MigrationGapException("journal-pending-append");
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".head")) throw new MigrationGapException("journal-orphan-head");
            return new VerifiedJournal(domain, [], 0, GenesisDigest(domain).ToLowerInvariant());
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new MigrationGapException("legacy-source-link-invalid");
        var info = new FileInfo(path);
        if (info.Length > MaximumJournalBytes) throw new MigrationGapException("journal-size-limit");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            if (File.Exists(path + ".head")) throw new MigrationGapException("journal-orphan-head");
            return new VerifiedJournal(domain, [], 0, GenesisDigest(domain).ToLowerInvariant());
        }
        if (bytes[^1] != (byte)'\n') throw new MigrationGapException("journal-incomplete-tail");
        var lines = ParseLines(bytes);
        var markerIndex = lines.FindIndex(static line => HasFormatMarker(line.Text));
        if (markerIndex < 0) throw new MigrationGapException("journal-unsealed-plaintext");

        var sequence = 0L;
        var headDigest = GenesisDigest(domain);
        var records = new List<VerifiedJournalRecord>(lines.Count);
        for (var index = 0; index < markerIndex; index++)
        {
            var line = lines[index];
            sequence = checked(sequence + 1);
            headDigest = AdvanceLegacy(headDigest, line.Text);
            if (!string.IsNullOrWhiteSpace(line.Text))
                records.Add(new(line.Number, null, line.Text, true, line.Digest));
        }

        for (var index = markerIndex; index < lines.Count; index++)
        {
            var line = lines[index];
            var envelope = ParseEnvelope(line.Text);
            if (envelope is null || !string.Equals(envelope.Marker, FormatMarker, StringComparison.Ordinal) ||
                envelope.Version != FormatVersion || !string.Equals(envelope.Domain, domain, StringComparison.Ordinal) ||
                envelope.Sequence != checked(sequence + 1) || string.IsNullOrWhiteSpace(envelope.Kind) ||
                envelope.Kind.Length > 128 || envelope.Payload is null || !IsDigest(envelope.PreviousDigest) ||
                !IsDigest(envelope.Digest) || !IsDigest(envelope.AuthenticationCode) ||
                !FixedTimeHexEquals(envelope.PreviousDigest, headDigest))
                throw new MigrationGapException("journal-envelope-invalid");

            var firstAuthenticated = index == markerIndex;
            if (firstAuthenticated && markerIndex > 0 && !string.Equals(envelope.Kind, SealKind, StringComparison.Ordinal))
                throw new MigrationGapException("journal-legacy-seal-missing");
            if (!firstAuthenticated && string.Equals(envelope.Kind, SealKind, StringComparison.Ordinal))
                throw new MigrationGapException("journal-seal-invalid");

            var body = new JournalBody(
                envelope.Version,
                envelope.Domain,
                envelope.Sequence,
                envelope.Kind,
                envelope.PreviousDigest,
                envelope.Payload.Value);
            var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
            var expectedDigest = Convert.ToHexString(SHA256.HashData(bodyBytes));
            var expectedAuthentication = Convert.ToHexString(HMACSHA256.HashData(_integrityKey, bodyBytes));
            if (!FixedTimeHexEquals(envelope.Digest, expectedDigest) ||
                !FixedTimeHexEquals(envelope.AuthenticationCode, expectedAuthentication))
                throw new MigrationGapException("journal-authentication-failed");

            sequence = envelope.Sequence;
            headDigest = envelope.Digest;
            if (!string.Equals(envelope.Kind, SealKind, StringComparison.Ordinal))
                records.Add(new(
                    line.Number,
                    envelope.Kind,
                    envelope.Payload.Value.GetRawText(),
                    false,
                    line.Digest));
        }

        VerifyHead(sequence, headDigest);
        return new VerifiedJournal(domain, records, sequence, headDigest.ToLowerInvariant());
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_integrityKey);
        _disposed = true;
    }

    private void VerifyHead(long sequence, string digest)
    {
        var headPath = path + ".head";
        if (!File.Exists(headPath)) throw new MigrationGapException("journal-head-missing");
        if ((File.GetAttributes(headPath) & FileAttributes.ReparsePoint) != 0)
            throw new MigrationGapException("legacy-source-link-invalid");
        var bytes = File.ReadAllBytes(headPath);
        if (bytes.Length is 0 or > 4096) throw new MigrationGapException("journal-head-invalid");
        JournalAnchor? anchor;
        try { anchor = JsonSerializer.Deserialize<JournalAnchor>(bytes, JsonOptions); }
        catch (JsonException) { throw new MigrationGapException("journal-head-invalid"); }
        if (anchor is null || anchor.Version != FormatVersion ||
            !string.Equals(anchor.Domain, domain, StringComparison.Ordinal) || anchor.Sequence <= 0 ||
            !IsDigest(anchor.Digest) || !IsDigest(anchor.AuthenticationCode))
            throw new MigrationGapException("journal-head-invalid");
        var body = new JournalAnchorBody(anchor.Version, anchor.Domain, anchor.Sequence, anchor.Digest);
        var expectedAuthentication = Convert.ToHexString(HMACSHA256.HashData(
            _integrityKey,
            JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions)));
        if (!FixedTimeHexEquals(anchor.AuthenticationCode, expectedAuthentication))
            throw new MigrationGapException("journal-head-authentication-failed");
        if (anchor.Sequence != sequence || !FixedTimeHexEquals(anchor.Digest, digest))
            throw new MigrationGapException("journal-head-mismatch");
    }

    private static List<JournalLine> ParseLines(byte[] bytes)
    {
        var lines = new List<JournalLine>();
        var start = 0;
        var number = 1;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n') continue;
            var raw = bytes.AsSpan(start, index - start);
            if (raw.Length > 0 && raw[^1] == (byte)'\r') raw = raw[..^1];
            string text;
            try { text = StrictUtf8.GetString(raw); }
            catch (DecoderFallbackException) { throw new MigrationGapException("journal-utf8-invalid"); }
            lines.Add(new JournalLine(
                number++,
                text,
                Convert.ToHexStringLower(SHA256.HashData(raw))));
            start = index + 1;
        }
        return lines;
    }

    private static JournalEnvelope? ParseEnvelope(string value)
    {
        try { return JsonSerializer.Deserialize<JournalEnvelope>(value, JsonOptions); }
        catch (JsonException) { throw new MigrationGapException("journal-envelope-invalid"); }
    }

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

    private static string AdvanceLegacy(string previousDigest, string text)
    {
        var lineBytes = Encoding.UTF8.GetBytes(text);
        var prefix = Encoding.UTF8.GetBytes(previousDigest + "\n" + lineBytes.Length + "\n");
        var chained = new byte[prefix.Length + lineBytes.Length];
        prefix.CopyTo(chained, 0);
        lineBytes.CopyTo(chained, prefix.Length);
        return Convert.ToHexString(SHA256.HashData(chained));
    }

    private static string GenesisDigest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("digitalbrain-journal-genesis\n" + value)));

    private static bool IsDigest(string? value) =>
        value is { Length: 64 } && value.All(static character => Uri.IsHexDigit(character));

    private static bool FixedTimeHexEquals(string first, string second)
    {
        if (!IsDigest(first) || !IsDigest(second)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(first), Convert.FromHexString(second));
    }

    private sealed record JournalLine(int Number, string Text, string Digest);
    private sealed record JournalBody(
        int Version,
        string Domain,
        long Sequence,
        string Kind,
        string PreviousDigest,
        JsonElement Payload);
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
    private sealed record JournalAnchor(
        int Version,
        string Domain,
        long Sequence,
        string Digest,
        string AuthenticationCode);
}
