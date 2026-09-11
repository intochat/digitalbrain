namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.write-preview")]
public sealed record SalesforceWritePreview(
    [property: Id(0)] string PreviewId,
    [property: Id(1)] string Tool,
    [property: Id(2)] string ToolSchemaHash,
    [property: Id(3)] string Arguments,
    [property: Id(4)] DateTimeOffset ExpiresAt,
    [property: Id(5)] string? RecordId = null);
