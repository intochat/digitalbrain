namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.read-schema")]
public sealed record ReadSalesforceSchema([property: Id(0)] string? ObjectName = null);