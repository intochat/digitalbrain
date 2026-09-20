using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.write-prepared")]
public sealed record SalesforceWritePrepared([property: Id(0)] SalesforceWritePreview Preview) : Signal;