using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.write-uncertain")]
public sealed record SalesforceWriteUncertain([property: Id(0)] string PreviewId) : Signal;