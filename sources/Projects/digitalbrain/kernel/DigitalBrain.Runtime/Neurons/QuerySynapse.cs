namespace DigitalBrain.Runtime.Neurons;

[GenerateSerializer]
public sealed record QuerySynapse([property: Id(1)] string Method,
    [property: Id(2)] string? ReturnTypeName
) : Synapse;
