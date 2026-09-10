namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.state")]
internal sealed record SalesforceState(
    [property: Id(0)] string? AccessToken = null,
    [property: Id(1)] string? RefreshToken = null,
    [property: Id(2)] DateTimeOffset? ExpiresAt = null,
    [property: Id(3)] string? InstanceUrl = null,
    [property: Id(4)] SalesforceWritePreview? PendingWrite = null);
