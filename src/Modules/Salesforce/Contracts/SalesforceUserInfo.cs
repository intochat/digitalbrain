using System.Text.Json;

namespace DigitalBrain.Salesforce;

[GenerateSerializer]
[Alias("db.salesforce.user-info")]
public sealed record SalesforceUserInfo(
    [property: Id(0)] JsonElement Content);
