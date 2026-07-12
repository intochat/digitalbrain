using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Kernel.Runtime;

public sealed record RuntimeMigrationMarker(
    int SchemaVersion,
    string SourceDigest,
    string MigrationId,
    string ExpectedDigest,
    int ConversationCount,
    int TurnCount,
    int ActiveOperationCount,
    int TerminalOperationCount);

public static class RuntimeMigrationMarkerCodec
{
    private const int Version = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static byte[] Protect(
        RuntimeMigrationMarker marker,
        string binding,
        IRuntimeStateKeyRing keys)
    {
        ValidateMarker(marker);
        ValidateBinding(binding);
        var key = RequireKey(keys, keys.ActiveKekVersion);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(marker, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using (var aes = new AesGcm(key.Span, tag.Length))
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(binding));
            var body = new MarkerEnvelopeBody(
                Version,
                keys.ActiveKekVersion,
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
            var authenticationCode = HMACSHA256.HashData(
                RequireSigningKey(keys).Span,
                JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions));
            try
            {
                return JsonSerializer.SerializeToUtf8Bytes(new MarkerEnvelope(
                    body.Version,
                    body.KeyVersion,
                    body.Nonce,
                    body.Ciphertext,
                    body.Tag,
                    Convert.ToBase64String(authenticationCode)), JsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authenticationCode);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public static RuntimeMigrationMarker Unprotect(
        ReadOnlySpan<byte> data,
        string binding,
        IRuntimeStateKeyRing keys)
    {
        ValidateBinding(binding);
        MarkerEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MarkerEnvelope>(data, JsonOptions)
                       ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new RuntimeStateIntegrityException("migration marker envelope is invalid");
        }
        if (envelope.Version != Version || envelope.KeyVersion < 1 ||
            string.IsNullOrWhiteSpace(envelope.Nonce) || envelope.Nonce.Length > 64 ||
            string.IsNullOrWhiteSpace(envelope.Ciphertext) || envelope.Ciphertext.Length > 1_500_000 ||
            string.IsNullOrWhiteSpace(envelope.Tag) || envelope.Tag.Length > 64 ||
            string.IsNullOrWhiteSpace(envelope.AuthenticationCode) || envelope.AuthenticationCode.Length > 64)
            throw new RuntimeStateIntegrityException("migration marker envelope is invalid");
        byte[] nonce;
        byte[] ciphertext;
        byte[] tag;
        byte[] authenticationCode;
        try
        {
            nonce = Convert.FromBase64String(envelope.Nonce);
            ciphertext = Convert.FromBase64String(envelope.Ciphertext);
            tag = Convert.FromBase64String(envelope.Tag);
            authenticationCode = Convert.FromBase64String(envelope.AuthenticationCode);
        }
        catch (FormatException)
        {
            throw new RuntimeStateIntegrityException("migration marker envelope is invalid");
        }
        try
        {
            if (nonce.Length != 12 || tag.Length != 16 || authenticationCode.Length != 32 ||
                ciphertext.Length is 0 or > 1024 * 1024)
                throw new RuntimeStateIntegrityException("migration marker envelope is invalid");
            var body = new MarkerEnvelopeBody(
                envelope.Version,
                envelope.KeyVersion,
                envelope.Nonce,
                envelope.Ciphertext,
                envelope.Tag);
            var expectedAuthentication = HMACSHA256.HashData(
                RequireSigningKey(keys).Span,
                JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions));
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(authenticationCode, expectedAuthentication))
                    throw new RuntimeStateIntegrityException("migration marker authentication failed");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedAuthentication);
            }
            var plaintext = new byte[ciphertext.Length];
            try
            {
                try
                {
                    using var aes = new AesGcm(RequireKey(keys, envelope.KeyVersion).Span, tag.Length);
                    aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(binding));
                }
                catch (AuthenticationTagMismatchException)
                {
                    throw new RuntimeStateIntegrityException("migration marker authentication failed");
                }
                RuntimeMigrationMarker marker;
                try
                {
                    marker = JsonSerializer.Deserialize<RuntimeMigrationMarker>(plaintext, JsonOptions)
                             ?? throw new JsonException();
                }
                catch (JsonException)
                {
                    throw new RuntimeStateIntegrityException("migration marker payload is invalid");
                }
                ValidateMarker(marker);
                return marker;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(authenticationCode);
        }
    }

    private static ReadOnlyMemory<byte> RequireKey(IRuntimeStateKeyRing keys, int version)
    {
        if (!keys.TryGetKek(version, out var key) || key.Length != 32)
            throw new RuntimeStateIntegrityException("migration marker key is unavailable");
        return key;
    }

    private static ReadOnlyMemory<byte> RequireSigningKey(IRuntimeStateKeyRing keys)
    {
        if (keys.SigningKey.Length < 32)
            throw new RuntimeStateIntegrityException("migration marker signing key is unavailable");
        return keys.SigningKey;
    }

    private static byte[] AssociatedData(string binding) =>
        Encoding.UTF8.GetBytes("digitalbrain-runtime-migration-marker-v1\n" + binding);

    private static void ValidateBinding(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding) || binding.Length > 256)
            throw new ArgumentException("Migration marker binding is invalid.", nameof(binding));
    }

    private static void ValidateMarker(RuntimeMigrationMarker marker)
    {
        if (marker.SchemaVersion != 1 || !IsLowerDigest(marker.SourceDigest) ||
            !string.Equals(marker.MigrationId, "legacy-v2-" + marker.SourceDigest, StringComparison.Ordinal) ||
            !IsLowerDigest(marker.ExpectedDigest) || marker.ConversationCount < 0 || marker.TurnCount < 0 ||
            marker.ActiveOperationCount < 0 || marker.TerminalOperationCount < 0)
            throw new RuntimeStateIntegrityException("migration marker payload is invalid");
    }

    private static bool IsLowerDigest(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private sealed record MarkerEnvelopeBody(
        int Version,
        int KeyVersion,
        string Nonce,
        string Ciphertext,
        string Tag);

    private sealed record MarkerEnvelope(
        int Version,
        int KeyVersion,
        string Nonce,
        string Ciphertext,
        string Tag,
        string AuthenticationCode);
}
