namespace DigitalBrain.Salesforce;

/// <summary>Redeems a one-use login nonce; the instance URL must be HTTPS.</summary>
[GenerateSerializer]
[Alias("salesforce.connect-account")]
public sealed record ConnectSalesforceAccount(
    [property: Id(0)] string InstanceUrl,
    [property: Id(1)] int ExpiresInSeconds,
    [property: Id(2)] string Nonce,
    [property: Id(3)] string? SecretOwner = null);
