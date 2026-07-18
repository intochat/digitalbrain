using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.Protocol.Microsoft.Aspire;

[GenerateSerializer]
public record DistributedAppStarted(
    [property: Id(0)] string AppHostProjectPath,
    [property: Id(1)] bool Success,
    [property: Id(2)] string Message = ""
) : Synapse;
