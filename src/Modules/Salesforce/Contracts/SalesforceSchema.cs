using System.Text.Json;

namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("db.salesforce.schema")]
public sealed record SalesforceSchema([property: Id(0)] JsonElement Content);
