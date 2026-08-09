using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class OwnerApprovalSigner
{
    private const string AlgorithmName = "ES256-P1363";
    private const string NistP256CurveOid = "1.2.840.10045.3.1.7";
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly Func<byte[], byte[]> _sign;
    private readonly byte[] _publicKey;

    public OwnerApprovalSigner(Func<byte[], byte[]> sign, byte[] subjectPublicKeyInfo)
    {
        _sign = sign ?? throw new ArgumentNullException(nameof(sign));
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
        if (bytesRead != subjectPublicKeyInfo.Length ||
            verifier.KeySize != 256 ||
            !string.Equals(
                verifier.ExportParameters(includePrivateParameters: false).Curve.Oid.Value,
                NistP256CurveOid,
                StringComparison.Ordinal))
        {
            throw new CryptographicException("Owner approvals require a P-256 authority key.");
        }

        _publicKey = subjectPublicKeyInfo.ToArray();
    }

    public OwnerApproval Sign(OwnerApprovalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!IsStructurallyValid(payload))
        {
            throw new CryptographicException("The owner approval payload is not canonical or complete.");
        }

        var signature = _sign(CanonicalPayload(payload));
        if (signature.Length != 64)
        {
            throw new CryptographicException("The P-256 authority must return an IEEE-P1363 signature.");
        }

        return new OwnerApproval(
            payload,
            AlgorithmName,
            Convert.ToBase64String(_publicKey),
            Convert.ToBase64String(signature));
    }

    public bool Verify(OwnerApproval? approval)
    {
        try
        {
            if (approval?.Payload is null ||
                !string.Equals(approval.Algorithm, AlgorithmName, StringComparison.Ordinal) ||
                !IsStructurallyValid(approval.Payload) ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(approval.PublicKey),
                    _publicKey))
            {
                return false;
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return verifier.VerifyData(
                CanonicalPayload(approval.Payload),
                Convert.FromBase64String(approval.Signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static byte[] CanonicalPayload(OwnerApprovalPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJson);

    private static bool IsStructurallyValid(OwnerApprovalPayload payload) =>
        IsLowerHash(payload.CandidateId) &&
        IsLowerHash(payload.SourceHash) &&
        IsLowerHash(payload.AssemblyHash) &&
        IsLowerHash(payload.CandidateMetadataHash) &&
        IsLowerHash(payload.AttestationPayloadHash) &&
        string.Equals(payload.CandidateId, payload.SourceHash, StringComparison.Ordinal) &&
        payload.RunId is not null &&
        payload.RunId.StartsWith("run-", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(payload.OwnerId) &&
        IsFamily(payload.FamilyId);

    private static bool IsFamily(string? family)
    {
        try
        {
            _ = DigitalBrain.Poc.Runtime.CandidateFamilyId.Parse(family!);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsLowerHash(string? value) =>
        value is not null && value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
