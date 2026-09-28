namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.confirm-write")]
public sealed record ConfirmSalesforceWrite(
    [property: Id(0)] string PreviewId,
    [property: Id(1)] string ToolSchemaHash);