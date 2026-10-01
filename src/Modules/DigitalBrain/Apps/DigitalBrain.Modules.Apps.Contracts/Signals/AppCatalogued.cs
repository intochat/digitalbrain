using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.catalogued")]
public sealed record AppCatalogued(string AppId, string Version, DateTimeOffset At) : Signal;
