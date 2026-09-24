namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.package-operation")]
public sealed record PackageOperation([property: Id(0)] string Name, [property: Id(1)] string Description);
