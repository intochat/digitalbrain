namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.write-prepared")]
public sealed record SalesforceWritePrepared([property: Id(0)] SalesforceWritePreview Preview);
