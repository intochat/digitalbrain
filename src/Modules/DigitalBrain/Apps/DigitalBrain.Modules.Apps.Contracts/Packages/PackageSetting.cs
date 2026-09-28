namespace DigitalBrain.Apps;

// Settings are how an installer customizes a package without forking it. They never hold secrets.
[GenerateSerializer, Alias("apps.package-setting")]
public sealed record PackageSetting([property: Id(0)] string Name, [property: Id(1)] string Description, [property: Id(2)] string DefaultValue);
