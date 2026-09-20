namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.propose-edit")]
public sealed record ProposeEdit(
    [property: Id(0)] EditRequest Edit,
    [property: Id(1)] long? ExpectedVersion = null);