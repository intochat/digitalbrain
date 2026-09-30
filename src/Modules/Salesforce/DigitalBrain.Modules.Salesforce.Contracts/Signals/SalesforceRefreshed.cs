using DigitalBrain.Contracts;

namespace DigitalBrain.Salesforce.Signals;

[GenerateSerializer, Alias("salesforce.refreshed")]
public sealed record SalesforceRefreshed([property: Id(0)] SalesforceConnection Connection) : Signal;