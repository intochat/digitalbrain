using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;

namespace DigitalBrain.Hosting.Microsoft.Flutter;

[GenerateSerializer]
public sealed record StartFlutterClient(
    [property: Id(0)] string Target = "web-server",
    [property: Id(1)] string Arguments = ""
) : Synapse;