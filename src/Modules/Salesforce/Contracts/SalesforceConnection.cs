namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.connection")]
public sealed record SalesforceConnection(
    [property: Id(0)] bool Connected,
    [property: Id(1)] string? InstanceUrl,
    [property: Id(2)] DateTimeOffset? ExpiresAt);
