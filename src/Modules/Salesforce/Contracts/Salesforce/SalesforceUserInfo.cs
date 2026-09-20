namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.user-info")]
public sealed record SalesforceUserInfo([property: Id(0)] string Content);