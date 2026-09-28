using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.app-changed")]
public sealed record AppChanged([property: Id(0)] AppSnapshot App) : Signal;
