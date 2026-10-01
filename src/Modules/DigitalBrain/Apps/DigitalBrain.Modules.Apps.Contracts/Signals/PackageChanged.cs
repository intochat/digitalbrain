using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.package-changed")]
public sealed record PackageChanged([property: Id(0)] PackageId Package, [property: Id(1)] string? Head, [property: Id(2)] string? Published) : Signal;
