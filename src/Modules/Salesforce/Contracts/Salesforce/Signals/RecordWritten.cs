using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.record-written")]
public sealed record RecordWritten([property: Id(0)] SalesforceWritePreview Preview) : Signal;