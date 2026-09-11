namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.record-written")]
public sealed record RecordWritten([property: Id(0)] SalesforceWritePreview Preview);
