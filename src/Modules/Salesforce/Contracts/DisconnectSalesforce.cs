using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.disconnect")]
public sealed record DisconnectSalesforce(
    CommandId Id) : Command(Id);
