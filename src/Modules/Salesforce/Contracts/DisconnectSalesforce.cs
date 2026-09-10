using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

/// <summary>Disconnects Salesforce.</summary>
[GenerateSerializer]
[Alias("db.salesforce.disconnect")]
public sealed record DisconnectSalesforce(
    CommandId Id) : Command(Id);
