namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.propose-change")]
public sealed record ProposeChange([property: Id(0)] Guid OperationId, [property: Id(1)] PackageRevisionRef Source, [property: Id(2)] string Title);
