using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.Protocol.Microsoft.Aspire;

[GenerateSerializer]
public record RestartResource(
    [property: Id(0)] string ResourceName
) : Synapse;
