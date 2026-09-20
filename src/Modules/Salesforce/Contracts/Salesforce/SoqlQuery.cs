namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.soql-query")]
public sealed record SoqlQuery([property: Id(0)] string Query);