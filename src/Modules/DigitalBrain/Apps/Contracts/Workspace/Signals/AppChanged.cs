using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.runtime-changed")]
public sealed record AppChanged([property: Id(0)] long Revision, [property: Id(1)] AppStatus Status) : Signal;
