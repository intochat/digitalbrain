namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.configure-app")]
public sealed record ConfigureApp([property: Id(0)] Guid OperationId, [property: Id(1)] IReadOnlyDictionary<string, string> Settings,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Accounts = null);
