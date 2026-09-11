namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.write-requested")]
public sealed record SalesforceWriteRequested([property: Id(0)] SalesforceWritePreview Preview, [property: Id(1)] string InstanceUrl);
