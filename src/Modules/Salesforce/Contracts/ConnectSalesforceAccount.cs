using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

/// <summary>Connects a validated Salesforce account.</summary>
[GenerateSerializer]
[Alias("db.salesforce.connect-account")]
public sealed record ConnectSalesforceAccount(
    CommandId Id,
    [property: Id(0)] string AccessToken,
    [property: Id(1)] string? RefreshToken,
    [property: Id(2)] int ExpiresInSeconds,
    [property: Id(3)] string InstanceUrl) : Command(Id);
