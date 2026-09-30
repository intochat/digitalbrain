using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.disconnected")]
public sealed record SalesforceDisconnected : Signal;