namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.refreshed")]
public sealed record SalesforceRefreshed([property: Id(0)] SalesforceConnection Connection);
