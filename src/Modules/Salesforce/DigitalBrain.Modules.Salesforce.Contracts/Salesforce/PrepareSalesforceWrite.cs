namespace DigitalBrain.Salesforce;

[GenerateSerializer, Alias("salesforce.prepare-write")]
public sealed record PrepareSalesforceWrite(
    [property: Id(0)] string Tool,
    [property: Id(1)] string Arguments);