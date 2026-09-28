namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.schema")]
public sealed record SalesforceSchema([property: Id(0)] string Content);