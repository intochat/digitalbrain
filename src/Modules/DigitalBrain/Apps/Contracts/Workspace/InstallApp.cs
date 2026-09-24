namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.install-app")]
public sealed record InstallApp(
    [property: Id(0)] Guid OperationId,
    [property: Id(1)] PackageRevisionRef Revision,
    [property: Id(2)] IReadOnlyDictionary<string, string> Settings);
