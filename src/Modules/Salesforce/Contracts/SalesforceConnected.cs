namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.connected")]
public sealed record SalesforceConnected([property: Id(0)] SalesforceConnection Connection);
