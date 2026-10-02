using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.installed-changed")]
public sealed record AppsChanged([property: Id(0)] PackageId[] Packages) : Signal;
