using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalBrain.Capabilities.Identity;

/// <summary>What an issuer vouches for. <c>Amr</c> is how the issuer authenticated the person (<c>pwd</c>, <c>google</c>, <c>otp</c>, <c>api</c>).</summary>
/// <param name="Issuer">The issuer name (header <c>kid</c>, payload <c>iss</c>).</param>
/// <param name="Audience">The install origin the ticket is for.</param>
/// <param name="Email">The verified address.</param>
/// <param name="Name">The display name.</param>
/// <param name="EmailVerified">Must be true.</param>
/// <param name="Amr">The authentication method.</param>
/// <param name="Jti">The ticket id; the verifier owns replay.</param>
/// <param name="ExpiresAt">At most 60 seconds after signing.</param>
public sealed record ExternalSignInClaims(string Issuer, string Audience, string Email, string Name, bool EmailVerified,
    string Amr, string Jti, DateTimeOffset ExpiresAt);

/// <summary>
/// identity@1 external sign-in ticket, a byte-compatible port of the os type. The wire format, shared by the issuer that signs and the OS install that verifies: a compact JWS
/// signed with ES256 (P-256, SHA-256, IEEE P1363 signature). Header {"alg":"ES256","typ":"JWT","kid":<issuer name>};
/// payload {"iss","aud","exp","jti","email","name","email_verified","amr"}. A ticket lives at most 60 seconds and is
/// redeemed once; the verifier owns replay.
/// </summary>
public static class ExternalSignInTicket
{
    /// <summary>The longest ticket lifetime in seconds.</summary>
    public const int LifetimeSeconds = 60;
    /// <summary>The longest ticket accepted, in characters.</summary>
    public const int MaxTokenLength = 4096;
    private const string P256Oid = "1.2.840.10045.3.1.7";
    private static readonly string[] Methods = ["pwd", "google", "otp", "api"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The P-256 key from SubjectPublicKeyInfo DER in base64; throws for anything else.</summary>
    public static ECDsa ImportPublicKey(string spkiBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spkiBase64);
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(spkiBase64), out _);
            if (key.ExportParameters(false).Curve.Oid.Value != P256Oid) { throw new CryptographicException("The key is not a P-256 key."); }
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Signs <paramref name="claims"/>; throws <see cref="ArgumentException"/> for claims a verifier would refuse.</summary>
    public static string Sign(ExternalSignInClaims claims, ECDsa privateKey, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(clock);
        if (!Valid(claims, clock.GetUtcNow()))
        { throw new ArgumentException("A ticket needs a verified address, a known method and a lifetime of at most 60 seconds.", nameof(claims)); }
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new Header("ES256", "JWT", claims.Issuer), Json));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new Payload(claims.Issuer, claims.Audience, claims.ExpiresAt.ToUnixTimeSeconds(),
            claims.Jti, claims.Email, claims.Name, claims.EmailVerified, claims.Amr), Json));
        var body = header + "." + payload;
        return body + "." + Encode(privateKey.SignData(Encoding.ASCII.GetBytes(body), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>
    /// The claims of a ticket a trusted issuer signed for this audience, still within its lifetime; null for anything else.
    /// <paramref name="keyOf"/> answers the issuer's key and expected audience by issuer name, or null for an unknown issuer.
    /// </summary>
    public static ExternalSignInClaims? Verify(string? token, Func<string, (ECDsa Key, string Audience)?> keyOf, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) { return null; }
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3 || parts.Any(part => part.Length == 0)) { return null; }
            var header = JsonSerializer.Deserialize<Header>(Decode(parts[0]), Json);
            if (header is not { Alg: "ES256" } || header.Typ is not (null or "JWT") || string.IsNullOrEmpty(header.Kid)) { return null; }
            if (keyOf(header.Kid) is not { } trusted) { return null; }
            var signature = Decode(parts[2]);
            if (signature.Length != 64 ||
                !trusted.Key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            { return null; }
            var payload = JsonSerializer.Deserialize<Payload>(Decode(parts[1]), Json);
            if (payload is null || payload.Iss != header.Kid || payload.Aud is not { } audience || audience != trusted.Audience ||
                payload.Exp is < 0 or > 253_402_300_799) { return null; }
            var claims = new ExternalSignInClaims(header.Kid, audience, payload.Email ?? "", payload.Name ?? "", payload.EmailVerified,
                payload.Amr ?? "", payload.Jti ?? "", DateTimeOffset.FromUnixTimeSeconds(payload.Exp));
            return Valid(claims, clock.GetUtcNow()) ? claims : null;
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
        catch (ArgumentException) { return null; }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Trim and lowercase; null when empty, over 255 characters or without an @ between non-empty parts.</summary>
    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) { return null; }
        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 255 || normalized.Any(char.IsControl)) { return null; }
        var at = normalized.IndexOf('@');
        return at <= 0 || at == normalized.Length - 1 || normalized.IndexOf('@', at + 1) >= 0 ? null : normalized;
    }

    private static bool Valid(ExternalSignInClaims claims, DateTimeOffset now) =>
        !string.IsNullOrWhiteSpace(claims.Issuer) && claims.Issuer.Length <= 100 &&
        !string.IsNullOrWhiteSpace(claims.Audience) && claims.Audience.Length <= 500 &&
        claims.EmailVerified && Methods.Contains(claims.Amr, StringComparer.Ordinal) &&
        NormalizeEmail(claims.Email) is not null &&
        !string.IsNullOrWhiteSpace(claims.Jti) && claims.Jti.Length <= 128 &&
        claims.Name is { Length: <= 200 } && claims.ExpiresAt > now && claims.ExpiresAt <= now.AddSeconds(LifetimeSeconds);

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        if (value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') || value.Length % 4 == 1)
        { throw new FormatException("Invalid base64url."); }
        return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    }

    private sealed record Header(string? Alg, string? Typ, string? Kid);

    private sealed record Payload(string? Iss, string? Aud, long Exp, string? Jti, string? Email, string? Name,
        [property: JsonPropertyName("email_verified")] bool EmailVerified, string? Amr);
}
