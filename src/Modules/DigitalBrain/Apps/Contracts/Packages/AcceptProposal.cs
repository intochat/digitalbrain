namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.accept-proposal")]
public sealed record AcceptProposal([property: Id(0)] Guid OperationId, [property: Id(1)] int Number);
