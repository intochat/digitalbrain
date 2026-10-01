namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.upgrade-app")]
public sealed record UpgradeApp([property: Id(0)] Guid OperationId, [property: Id(1)] PackageRevisionRef Revision,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Accounts = null);
