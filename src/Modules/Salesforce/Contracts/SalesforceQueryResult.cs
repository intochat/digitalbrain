using System.Text.Json;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.query-result")]
public sealed record SalesforceQueryResult(
    [property: Id(0)] JsonElement Content,
    [property: Id(1)] int TotalSize);
