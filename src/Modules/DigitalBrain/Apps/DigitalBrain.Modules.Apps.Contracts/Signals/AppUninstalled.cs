using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.uninstalled")]
public sealed record AppUninstalled(string AppId, IReadOnlyList<string> KeptData) : Signal;
