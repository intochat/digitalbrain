using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class AttestationSigner
{
    private const string AlgorithmName = "ES256-P1363";
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly Func<byte[], byte[]> _sign;
    private readonly byte[] _publicKey;

    public AttestationSigner(Func<byte[], byte[]> sign, byte[] subjectPublicKeyInfo)
    {
        _sign = sign ?? throw new ArgumentNullException(nameof(sign));
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
        if (bytesRead != subjectPublicKeyInfo.Length || verifier.KeySize != 256)
        {
            throw new CryptographicException("Candidate attestations require a P-256 authority key.");
        }

        _publicKey = subjectPublicKeyInfo.ToArray();
    }

    public CandidateAttestation Sign(CandidateAttestationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!IsStructurallyValid(payload))
        {
            throw new CryptographicException("The candidate attestation payload is not canonical or complete.");
        }

        var signature = _sign(CanonicalPayload(payload));
        if (signature.Length != 64)
        {
            throw new CryptographicException("The P-256 authority must return an IEEE-P1363 signature.");
        }

        return new CandidateAttestation(
            payload,
            AlgorithmName,
            Convert.ToBase64String(_publicKey),
            Convert.ToBase64String(signature));
    }

    public bool Verify(CandidateAttestation? attestation)
    {
        try
        {
            if (attestation is null ||
                attestation.Payload is null ||
                !string.Equals(attestation.Algorithm, AlgorithmName, StringComparison.Ordinal) ||
                !IsStructurallyValid(attestation.Payload) ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(attestation.PublicKey),
                    _publicKey))
            {
                return false;
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return verifier.VerifyData(
                CanonicalPayload(attestation.Payload),
                Convert.FromBase64String(attestation.Signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static byte[] CanonicalPayload(CandidateAttestationPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJson);

    private static bool IsLowerHash(string? value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsStructurallyValid(CandidateAttestationPayload? payload)
    {
        try
        {
            if (payload is null || string.IsNullOrWhiteSpace(payload.FamilyId))
            {
                return false;
            }

            _ = CandidateFamilyId.Parse(payload.FamilyId);
            var hashes = new[]
            {
                payload.CandidateId,
                payload.SourceHash,
                payload.AssemblyHash,
                payload.CandidateMetadataHash,
                payload.ScenarioHash,
                payload.NormalizedAstHash,
                payload.FixedHeaderHash,
                payload.CompilerHash,
                payload.SdkHash,
                payload.ReferencesHash,
                payload.CapabilitiesHash,
                payload.ContractsHash,
                payload.StateSchemaHash,
            };
            var inputAliases = IsDistinctNonempty(payload.GrantedInputAliases);
            var candidateOutputAliases = IsDistinctNonempty(payload.GrantedCandidateOutputAliases);
            var trustedOutputAliases = payload.GrantedTrustedOutputAliases is not null &&
                payload.GrantedTrustedOutputAliases.SequenceEqual(
                    ["db.poc.chart.add-point.v1"],
                    StringComparer.Ordinal);
            var targetScopes = IsDistinctNonempty(payload.GrantedTargetScopes);
            var resolvedReferences = IsDistinctNonempty(payload.ResolvedReferences);
            var localPrefix = $"db.poc.family.{payload.FamilyId}.";
            return hashes.All(IsLowerHash) &&
                string.Equals(payload.CandidateId, payload.SourceHash, StringComparison.Ordinal) &&
                payload.RunId is not null &&
                payload.RunId.StartsWith("run-", StringComparison.Ordinal) &&
                payload.RunId.Length == 36 &&
                payload.RunId[4..].All(character =>
                    character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
                !string.IsNullOrWhiteSpace(payload.OwnerId) &&
                string.Equals(payload.Revision, $"quarantine-{payload.AssemblyHash}", StringComparison.Ordinal) &&
                string.Equals(payload.Status, "awaitingOwnerApproval", StringComparison.Ordinal) &&
                string.Equals(payload.SourcePath, "elon-chart.cs", StringComparison.Ordinal) &&
                string.Equals(payload.AssemblyPath, "module.dll", StringComparison.Ordinal) &&
                inputAliases &&
                candidateOutputAliases &&
                payload.GrantedCandidateOutputAliases!.All(alias =>
                    alias.StartsWith(localPrefix, StringComparison.Ordinal)) &&
                trustedOutputAliases &&
                targetScopes &&
                resolvedReferences;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsDistinctNonempty(IReadOnlyList<string>? values) =>
        values is not null &&
        values.Count != 0 &&
        values.All(value => !string.IsNullOrWhiteSpace(value)) &&
        values.Distinct(StringComparer.Ordinal).Count() == values.Count;
}
