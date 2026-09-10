using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

/// <summary>Redeems a one-use login nonce; the instance URL must be HTTPS.</summary>
[GenerateSerializer]
[Alias("db.salesforce.connect-account")]
public sealed record ConnectSalesforceAccount(
    CommandId Id,
    [property: Id(0)] string InstanceUrl,
    [property: Id(1)] int ExpiresInSeconds,
    [property: Id(2)] string Nonce) : Command(Id);
