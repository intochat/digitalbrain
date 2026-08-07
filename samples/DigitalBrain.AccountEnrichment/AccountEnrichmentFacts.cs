using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.AccountEnrichment;

[GenerateSerializer]
[Alias("db.account-enrichment.requested")]
[Description("Request account enrichment from an email")]
public sealed record EnrichAccountFromEmail(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string AccountId,
    [property: Id(3)] string GmailAccount) : Synapse;

[GenerateSerializer]
[Alias("db.account-enrichment.proposed")]
[Description("Account enrichment was proposed")]
public sealed record AccountEnrichmentProposed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string AccountId,
    [property: Id(3)] string Description,
    [property: Id(4)] string Fingerprint) : Synapse;

[GenerateSerializer]
[Alias("db.account-enrichment.completed")]
[Description("Account enrichment completed")]
public sealed record AccountEnriched(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string MessageId,
    [property: Id(2)] string AccountId,
    [property: Id(3)] string Description) : Synapse;

