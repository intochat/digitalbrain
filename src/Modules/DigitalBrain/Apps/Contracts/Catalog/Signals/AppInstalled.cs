using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.installed")]
public sealed record AppInstalled(string AppId, string Version, DateTimeOffset At) : Signal;
