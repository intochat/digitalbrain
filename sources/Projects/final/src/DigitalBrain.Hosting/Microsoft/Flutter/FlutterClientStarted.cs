using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;

namespace DigitalBrain.Hosting.Microsoft.Flutter;

[GenerateSerializer]
public sealed record FlutterClientStarted(
    [property: Id(0)] string Target,
    [property: Id(1)] bool Success,
    [property: Id(2)] string Message = "",
    [property: Id(3)] string? Endpoint = null
) : Synapse;