namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.query-result")]
public sealed record SalesforceQueryResult(
    [property: Id(0)] string Content,
    [property: Id(1)] int TotalSize);