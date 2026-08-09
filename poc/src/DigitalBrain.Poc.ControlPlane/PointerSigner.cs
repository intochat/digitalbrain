using System.Security.Cryptography;
using System.Text.Json;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.ControlPlane;

public sealed class PointerSigner
{
    private const string AlgorithmName = "ES256-P1363";
    private const string NistP256CurveOid = "1.2.840.10045.3.1.7";
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly Func<byte[], byte[]> _sign;
    private readonly byte[] _publicKey;

    public PointerSigner(Func<byte[], byte[]> sign, byte[] subjectPublicKeyInfo)
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
            throw new CryptographicException("Active pointers require a P-256 control-plane key.");
        }

        _publicKey = subjectPublicKeyInfo.ToArray();
    }

    public ActiveCandidatePointer Sign(ActiveCandidatePointer pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        var payload = Payload(pointer);
        if (!IsStructurallyValid(payload))
        {
            throw new CryptographicException("The active candidate pointer is not canonical or complete.");
        }

        var canonical = CanonicalPayload(payload);
        var signature = _sign(canonical);
        if (signature.Length != 64)
        {
            throw new CryptographicException("The P-256 authority must return an IEEE-P1363 signature.");
        }

        return pointer with
        {
            PayloadHash = Hash(canonical),
            Algorithm = AlgorithmName,
            PublicKey = Convert.ToBase64String(_publicKey),
            Signature = Convert.ToBase64String(signature),
        };
    }

    public HostAuthorityDelegation SignHostAuthorityDelegation(
        HostAuthorityDelegationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!IsStructurallyValid(payload))
        {
            throw new CryptographicException("The host-authority delegation is not canonical or complete.");
        }

        var signature = _sign(CanonicalPayload(payload));
        if (signature.Length != 64)
        {
            throw new CryptographicException("The P-256 authority must return an IEEE-P1363 signature.");
        }

        return new HostAuthorityDelegation(
            payload,
            AlgorithmName,
            Convert.ToBase64String(_publicKey),
            Convert.ToBase64String(signature));
    }

    public bool Verify(ActiveCandidatePointer? pointer)
    {
        try
        {
            if (pointer is null ||
                !string.Equals(pointer.Algorithm, AlgorithmName, StringComparison.Ordinal) ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(pointer.PublicKey),
                    _publicKey))
            {
                return false;
            }

            var payload = Payload(pointer);
            if (!IsStructurallyValid(payload))
            {
                return false;
            }

            var canonical = CanonicalPayload(payload);
            if (!string.Equals(pointer.PayloadHash, Hash(canonical), StringComparison.Ordinal))
            {
                return false;
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return verifier.VerifyData(
                canonical,
                Convert.FromBase64String(pointer.Signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool VerifyHostAuthorityDelegation(HostAuthorityDelegation? delegation)
    {
        try
        {
            if (delegation?.Payload is null ||
                !string.Equals(delegation.Algorithm, AlgorithmName, StringComparison.Ordinal) ||
                !IsStructurallyValid(delegation.Payload) ||
                !CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(delegation.PublicKey),
                    _publicKey))
            {
                return false;
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(_publicKey, out _);
            return verifier.VerifyData(
                CanonicalPayload(delegation.Payload),
                Convert.FromBase64String(delegation.Signature),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string ActiveSelectionHash(IEnumerable<string> sourceHashes)
    {
        ArgumentNullException.ThrowIfNull(sourceHashes);
        var canonical = string.Join(
            "\n",
            sourceHashes
                .Select(value => value.ToLowerInvariant())
                .Order(StringComparer.Ordinal));
        return Hash(System.Text.Encoding.UTF8.GetBytes(canonical));
    }

    private static ActiveCandidatePointerPayload Payload(ActiveCandidatePointer pointer) =>
        new(
            pointer.OwnerId,
            pointer.FamilyId,
            pointer.CandidateSourceHash,
            pointer.PreviousCandidateSourceHash,
            pointer.ParentPayloadHash,
            pointer.Version);

    private static byte[] CanonicalPayload(ActiveCandidatePointerPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJson);

    private static byte[] CanonicalPayload(HostAuthorityDelegationPayload payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, CanonicalJson);

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool IsStructurallyValid(ActiveCandidatePointerPayload payload)
    {
        try
        {
            _ = CandidateFamilyId.Parse(payload.FamilyId);
            return !string.IsNullOrWhiteSpace(payload.OwnerId) &&
                IsLowerHash(payload.CandidateSourceHash) &&
                IsLowerHash(payload.PreviousCandidateSourceHash) &&
                IsLowerHash(payload.ParentPayloadHash) &&
                payload.Version > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsStructurallyValid(HostAuthorityDelegationPayload payload) =>
        payload.RunId is { Length: 36 } &&
        payload.RunId.StartsWith("run-", StringComparison.Ordinal) &&
        payload.RunId[4..].All(Uri.IsHexDigit) &&
        IsLowerHash(payload.ExpectedHeadPayloadHash) &&
        IsLowerHash(payload.ActiveSelectionHash);

    private static bool IsLowerHash(string? value) =>
        value is not null && value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
