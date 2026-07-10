using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed class OAuthStateKeyRing
{
    private readonly IReadOnlyDictionary<int, byte[]> _keys;

    public OAuthStateKeyRing(int currentVersion, IReadOnlyDictionary<int, byte[]> keys)
    {
        if (!keys.TryGetValue(currentVersion, out var current) || current.Length < 32)
        {
            throw new ArgumentException("The current OAuth state HMAC key must contain at least 256 bits.", nameof(keys));
        }

        if (keys.Any(pair => pair.Key < 1 || pair.Value.Length < 32))
        {
            throw new ArgumentException("Every OAuth state key must have a positive version and at least 256 bits.", nameof(keys));
        }

        CurrentVersion = currentVersion;
        _keys = keys.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public int CurrentVersion { get; }

    public string CreateState()
        => $"v{CurrentVersion}.{Base64Url.Encode(RandomNumberGenerator.GetBytes(32))}";

    public OAuthFlowKey DeriveFlowKey(string state)
    {
        var version = ParseVersion(state);
        if (!_keys.TryGetValue(version, out var key))
        {
            throw new InvalidOperationException("OAuth state key version is not configured.");
        }

        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(state));
        return new OAuthFlowKey(version, Base64Url.Encode(digest));
    }

    public static int ParseVersion(string state)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length < 5 || state[0] != 'v')
        {
            throw new ArgumentException("OAuth state has an invalid format.", nameof(state));
        }

        var separator = state.IndexOf('.');
        if (separator < 2 || !int.TryParse(state.AsSpan(1, separator - 1), out var version) || version < 1)
        {
            throw new ArgumentException("OAuth state has an invalid key version.", nameof(state));
        }

        var random = Base64Url.Decode(state[(separator + 1)..]);
        if (random.Length != 32)
        {
            throw new ArgumentException("OAuth state must contain 256 bits of randomness.", nameof(state));
        }

        return version;
    }
}

public static class Pkce
{
    public static string CreateVerifier() => Base64Url.Encode(RandomNumberGenerator.GetBytes(32));

    public static string CreateS256Challenge(string verifier)
        => Base64Url.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(normalized);
    }
}
