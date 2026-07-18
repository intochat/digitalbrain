namespace DigitalBrain.Protocol.Domain.Events;

[GenerateSerializer]
public sealed record DynamicSynapse(
    [property: Id(0)] string TypeName,
    [property: Id(1)] Dictionary<string, string> Payload
) : Synapse;
