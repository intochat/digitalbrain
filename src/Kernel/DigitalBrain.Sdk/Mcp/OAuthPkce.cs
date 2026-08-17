using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Modules.Sdk.Mcp;

internal static class OAuthPkce
{
    internal const string ChallengeMethodS256 = "S256";

    internal static (string Verifier, string Challenge) CreateS256Pair()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(verifierBytes);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    internal static string Base64Url(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
