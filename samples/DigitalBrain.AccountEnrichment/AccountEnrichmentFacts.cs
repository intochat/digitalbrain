using DigitalBrain.Abstractions;

namespace DigitalBrain.AccountEnrichment;

[GenerateSerializer]
[Alias("db.account-enrichment.requested")]
public sealed record EnrichAccountFromEmail(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string AccountId,
    [property: Id(3)] string GmailAccount) : Synapse;

[GenerateSerializer]
[Alias("db.account-enrichment.proposed")]
public sealed record AccountEnrichmentProposed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string AccountId,
    [property: Id(3)] string Description,
    [property: Id(4)] string Fingerprint) : Synapse;

[GenerateSerializer]
[Alias("db.account-enrichment.completed")]
public sealed record AccountEnriched(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string AccountId,
    [property: Id(3)] string Description) : Synapse;
