using System.Security.Cryptography;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Host;

public sealed class TestOwnerAuthority
{
    private readonly ECDsa _attestationKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _approvalKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _pointerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, OwnerSession> _byOwner =
        new(StringComparer.Ordinal);

    public TestOwnerAuthority()
    {
        Add("owner-a");
        Add("owner-b");
    }

    public OwnerSession SessionFor(string ownerId) =>
        _byOwner.TryGetValue(ownerId, out var session)
            ? session
            : throw new KeyNotFoundException($"No fixed test owner session exists for '{ownerId}'.");

    public AuthenticatedPrincipal PrincipalForTest(string ownerId) =>
        new(SessionFor(ownerId).OwnerId);

    internal static TestOwnerAuthority FromExportedSessions(
        IReadOnlyDictionary<string, string> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var authority = new TestOwnerAuthority();
        authority._byOwner.Clear();
        foreach (var (token, ownerId) in sessions)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(ownerId))
            {
                throw new InvalidDataException("An exported owner session is malformed.");
            }

            authority._byOwner.Add(ownerId, new OwnerSession(ownerId, token));
        }

        return authority;
    }

    public IReadOnlyDictionary<string, string> ExportSessions() =>
        _byOwner.Values.ToDictionary(
            session => session.Token,
            session => session.OwnerId,
            StringComparer.Ordinal);

    public bool TryResolveToken(string opaqueToken, out OwnerSession session)
    {
        session = _byOwner.Values.SingleOrDefault(
            candidate => string.Equals(candidate.OpaqueToken, opaqueToken, StringComparison.Ordinal))!;
        return session is not null;
    }

    public AttestationSigner CreateAttestationSigner() => new(
        payload => _attestationKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
        _attestationKey.ExportSubjectPublicKeyInfo());

    public OwnerApprovalSigner CreateOwnerApprovalSigner() => new(
        payload => _approvalKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
        _approvalKey.ExportSubjectPublicKeyInfo());

    public PointerSigner CreatePointerSigner() => new(
        payload => _pointerKey.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation),
        _pointerKey.ExportSubjectPublicKeyInfo());

    public string AttestationPublicKey =>
        Convert.ToBase64String(_attestationKey.ExportSubjectPublicKeyInfo());

    public string ApprovalPublicKey =>
        Convert.ToBase64String(_approvalKey.ExportSubjectPublicKeyInfo());

    public string PointerPublicKey =>
        Convert.ToBase64String(_pointerKey.ExportSubjectPublicKeyInfo());

    private void Add(string ownerId)
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToHexString(bytes).ToLowerInvariant();
        _byOwner.Add(ownerId, new OwnerSession(ownerId, token));
    }
}
