using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Client;

/// <summary>
/// Claims carried within a signed session bearer token.
/// </summary>
public sealed record SessionClaims(string Brain, string Session, DateTimeOffset Expires, string? App = null, string? AccountId = null);

/// <summary>
/// HMAC-SHA256 session token generation and verification for runtime edge communication.
/// </summary>
public sealed class SessionTokens
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);
    private readonly byte[] _key;
    private readonly TimeProvider _timeProvider;

    public SessionTokens(byte[] key, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 16) { throw new ArgumentException("Session token key must have at least 16 bytes.", nameof(key)); }
        _key = (byte[])key.Clone();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static string Issue(byte[] secret, string brainId, string sessionId, TimeSpan? ttl = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrWhiteSpace(brainId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var clock = time ?? TimeProvider.System;
        var expires = clock.GetUtcNow() + (ttl ?? DefaultLifetime);
        var claims = new SessionClaims(brainId, sessionId, expires);
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(claims, JsonSerializerOptions.Web);
        var payloadB64 = Base64Url(payloadBytes);
        var signature = HMACSHA256.HashData(secret, payloadBytes);
        var signatureB64 = Base64Url(signature);
        return payloadB64 + "." + signatureB64;
    }

    public static string Issue(string secretBase64, string brainId, string sessionId, TimeSpan? ttl = null, TimeProvider? time = null)
        => Issue(Convert.FromBase64String(secretBase64), brainId, sessionId, ttl, time);

    public string Issue(string brainId, string sessionId, TimeSpan? ttl = null)
        => Issue(_key, brainId, sessionId, ttl, _timeProvider);

    public SessionClaims? Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }
        var parts = token.Split('.');
        if (parts.Length != 2) { return null; }
        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            signatureBytes = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        var expectedSignature = HMACSHA256.HashData(_key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signatureBytes))
        {
            return null;
        }

        SessionClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize<SessionClaims>(payloadBytes, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }

        if (claims is null || claims.Expires <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return claims;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }
}
