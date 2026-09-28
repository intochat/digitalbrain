using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.connection-rejected")]
public sealed record SalesforceConnectionRejected([property: Id(0)] string Reason) : Signal;