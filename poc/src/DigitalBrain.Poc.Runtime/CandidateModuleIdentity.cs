using System.Security.Cryptography;

namespace DigitalBrain.Poc.Runtime;

public sealed record CandidateModuleIdentity
{
    public CandidateModuleIdentity(
        string assemblySha256,
        string sourceSha256,
        string evidenceSha256)
    {
        AssemblySha256 = NormalizeHash(assemblySha256, nameof(assemblySha256));
        SourceSha256 = NormalizeHash(sourceSha256, nameof(sourceSha256));
        EvidenceSha256 = NormalizeHash(evidenceSha256, nameof(evidenceSha256));
    }

    public string AssemblySha256 { get; }

    public string SourceSha256 { get; }

    public string EvidenceSha256 { get; }

    public static CandidateModuleIdentity FromVerifiedBytes(
        byte[] assemblyBytes,
        byte[] sourceBytes,
        byte[] evidenceBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(evidenceBytes);
        return new CandidateModuleIdentity(
            Convert.ToHexString(SHA256.HashData(assemblyBytes)),
            Convert.ToHexString(SHA256.HashData(sourceBytes)),
            Convert.ToHexString(SHA256.HashData(evidenceBytes)));
    }

    private static string NormalizeHash(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new FormatException($"{parameterName} must be a SHA-256 hexadecimal digest.");
        }

        return value.ToLowerInvariant();
    }
}
