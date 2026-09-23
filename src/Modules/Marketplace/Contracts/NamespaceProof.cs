namespace DigitalBrain.Marketplace;

// Proof that a publisher owns the namespace it publishes under, by answering a DNS TXT challenge
// (the "namespace proof"). The authority is an adapter: production reads real DNS; tests use a fake.
public enum NamespaceProofOutcome
{
    Verified = 0,
    RecordMissing = 1,
    TokenMismatch = 2,
    DomainBlocked = 3,
}

[GenerateSerializer, Alias("marketplace.namespace-challenge")]
public sealed record NamespaceProofChallenge
{
    [Id(0)] public required string Namespace { get; init; }
    [Id(1)] public required string Domain { get; init; }
    [Id(2)] public required string RecordName { get; init; }
    [Id(3)] public required string Token { get; init; }
}

[GenerateSerializer, Alias("marketplace.namespace-proof")]
public sealed record NamespaceProofEvidence
{
    [Id(0)] public required string Namespace { get; init; }
    [Id(1)] public required NamespaceProofOutcome Outcome { get; init; }
    [Id(2)] public required DateTimeOffset CheckedAt { get; init; }

    public bool Verified => Outcome == NamespaceProofOutcome.Verified;
}

public interface INamespaceProofAuthority
{
    NamespaceProofChallenge IssueChallenge(string publisherNamespace, string domain);

    NamespaceProofEvidence Verify(NamespaceProofChallenge challenge);
}
