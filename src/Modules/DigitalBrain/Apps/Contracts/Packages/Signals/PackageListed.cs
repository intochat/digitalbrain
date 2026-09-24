using DigitalBrain.Contracts;

namespace DigitalBrain.Apps.Signals;

[GenerateSerializer, Alias("apps.package-listed")]
public sealed record PackageListed([property: Id(0)] PackageListing Listing) : Signal;
