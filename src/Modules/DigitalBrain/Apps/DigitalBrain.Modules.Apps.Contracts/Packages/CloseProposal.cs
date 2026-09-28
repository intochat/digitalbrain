namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.close-proposal")]
public sealed record CloseProposal([property: Id(0)] Guid OperationId, [property: Id(1)] int Number);
