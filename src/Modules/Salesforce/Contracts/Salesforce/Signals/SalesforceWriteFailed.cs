using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.write-failed")]
public sealed record SalesforceWriteFailed([property: Id(0)] string PreviewId) : Signal;