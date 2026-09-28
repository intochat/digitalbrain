namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-revision-ref")]
public sealed record PackageRevisionRef([property: Id(0)] PackageId Package, [property: Id(1)] string Revision);
