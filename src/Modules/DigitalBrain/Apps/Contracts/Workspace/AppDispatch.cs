using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.dispatch")]
public sealed record AppDispatch([property: Id(0)] Guid OperationId, [property: Id(1)] string Signal, [property: Id(2)] string Value);
