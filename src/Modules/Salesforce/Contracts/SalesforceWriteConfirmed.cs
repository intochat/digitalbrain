namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.write-confirmed")]
public sealed record SalesforceWriteConfirmed([property: Id(0)] string PreviewId, [property: Id(1)] string ToolSchemaHash);
