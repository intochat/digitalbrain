namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.write-failed")]
public sealed record SalesforceWriteFailed([property: Id(0)] string PreviewId);
