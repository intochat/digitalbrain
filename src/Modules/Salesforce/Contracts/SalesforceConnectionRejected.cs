namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.connection-rejected")]
public sealed record SalesforceConnectionRejected([property: Id(0)] string Reason);
