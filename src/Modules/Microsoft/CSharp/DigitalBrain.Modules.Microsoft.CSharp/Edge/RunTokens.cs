using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

internal sealed record RunTokenClaims(string File, string Run, DateTimeOffset Expires);

// A run's bearer token for the script edge: the file and run it speaks for, signed with HMAC-SHA256.
// Development signs with a key made at startup; production must configure one shared by every silo.
internal sealed class RunTokens(IOptions<CSharpOptions> options, TimeProvider time)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly byte[] _key = options.Value.RunTokenKey is { Length: > 0 } configured
        ? Convert.FromBase64String(configured)
        : RandomNumberGenerator.GetBytes(32);

    public string Issue(string file, string run)
    {
        var claims = JsonSerializer.SerializeToUtf8Bytes(new RunTokenClaims(file, run, time.GetUtcNow() + Lifetime), JsonSerializerOptions.Web);
        return Base64Url(claims) + "." + Base64Url(HMACSHA256.HashData(_key, claims));
    }

    public RunTokenClaims? Validate(string? token)
    {
        if (token?.Split('.') is not [var payload, var signature]) { return null; }
        byte[] claims;
        try { claims = FromBase64Url(payload); }
        catch (FormatException) { return null; }
        byte[] presented;
        try { presented = FromBase64Url(signature); }
        catch (FormatException) { return null; }
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, claims), presented)) { return null; }
        var parsed = JsonSerializer.Deserialize<RunTokenClaims>(claims, JsonSerializerOptions.Web);
        return parsed is not null && parsed.Expires > time.GetUtcNow() ? parsed : null;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
    }
}
