using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.confirm-write")]
public sealed record ConfirmSalesforceWrite(
    CommandId Id,
    [property: Id(0)] string PreviewId,
    [property: Id(1)] string ToolSchemaHash) : Command(Id);
