namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.write-uncertain")]
public sealed record SalesforceWriteUncertain([property: Id(0)] string PreviewId);
