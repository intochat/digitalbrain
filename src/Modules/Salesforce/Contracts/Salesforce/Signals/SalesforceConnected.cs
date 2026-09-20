using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.connected")]
public sealed record SalesforceConnected([property: Id(0)] SalesforceConnection Connection) : Signal;