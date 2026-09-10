using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.prepare-write")]
public sealed record PrepareSalesforceWrite(
    CommandId Id,
    [property: Id(0)] string Tool,
    [property: Id(1)] string Arguments) : Command(Id);
