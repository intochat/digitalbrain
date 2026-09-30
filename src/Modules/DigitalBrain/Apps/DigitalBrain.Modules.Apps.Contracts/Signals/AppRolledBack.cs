using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.rolled-back")]
public sealed record AppRolledBack(string AppId, string Version) : Signal;
