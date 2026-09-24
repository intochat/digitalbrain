using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("DigitalBrain.Activated")]
public sealed record DigitalBrainActivated([property: Id(0)] string WorkspaceId, [property: Id(1)] long Generation) : Signal;
