using DigitalBrain.Protocol.Domain.Events;

namespace DigitalBrain.Protocol.Microsoft.Aspire;

[GenerateSerializer]
public record StartDistributedApp(
    [property: Id(0)] string AppHostProjectPath,
    [property: Id(1)] string Arguments = ""
) : Synapse;
