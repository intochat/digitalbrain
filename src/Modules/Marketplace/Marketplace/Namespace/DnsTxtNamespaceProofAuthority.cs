using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DigitalBrain.Marketplace;

// DNS-TXT namespace proof behind an adapter. The challenge token is derived from the namespace and
// domain; it is answered by a TXT record a real deployment reads from DNS, while tests publish the
// record into the in-memory zone by calling PublishRecord.
public sealed class DnsTxtNamespaceProofAuthority : INamespaceProofAuthority
{
    public const string RecordPrefix = "_intochat-challenge.";

    private static readonly ConcurrentDictionary<string, string> Zone = new(StringComparer.OrdinalIgnoreCase);

    public NamespaceProofChallenge IssueChallenge(string publisherNamespace, string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var recordName = RecordPrefix + domain;
        var token = "intochat-proof=" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{publisherNamespace}|{domain}"))).ToLowerInvariant();
        return new NamespaceProofChallenge
        {
            Namespace = publisherNamespace,
            Domain = domain,
            RecordName = recordName,
            Token = token,
        };
    }

    public static void PublishRecord(string recordName, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Zone[recordName] = token;
    }

    public static void Reset() => Zone.Clear();

    public NamespaceProofEvidence Verify(NamespaceProofChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var outcome = Zone.TryGetValue(challenge.RecordName, out var token)
            ? string.Equals(token, challenge.Token, StringComparison.Ordinal)
                ? NamespaceProofOutcome.Verified
                : NamespaceProofOutcome.TokenMismatch
            : NamespaceProofOutcome.RecordMissing;
        return new NamespaceProofEvidence
        {
            Namespace = challenge.Namespace,
            Outcome = outcome,
            CheckedAt = DateTimeOffset.UtcNow,
        };
    }
}
