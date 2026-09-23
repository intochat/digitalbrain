using DigitalBrain.Contracts.Types;

namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.state")]
internal sealed record SalesforceState(
    [property: Id(0)] SecretRef? AccessToken = null,
    [property: Id(1)] SecretRef? RefreshToken = null,
    [property: Id(2)] DateTimeOffset? ExpiresAt = null,
    [property: Id(3)] string? InstanceUrl = null,
    [property: Id(4)] SalesforceWritePreview? PendingWrite = null,
    [property: Id(5)] SalesforceWritePreview? SubmittingWrite = null);
