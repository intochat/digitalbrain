using DigitalBrain.Operations;

namespace DigitalBrain.Capabilities.Storage;

/// <summary>A storage write operation started.</summary>
/// <param name="Operation">The operation id returned by <see cref="IStorage.Write"/>.</param>
/// <param name="Plan">The plan being carried out.</param>
[Signal("storage.operation.started")]
public sealed record OperationStarted(OperationId Operation, WritePlan Plan) : Signal;

/// <summary>A storage write operation progressed.</summary>
/// <param name="Operation">The operation id.</param>
/// <param name="Fraction">The fraction done, 0..1.</param>
[Signal("storage.operation.progress")]
public sealed record OperationProgress(OperationId Operation, double Fraction) : Signal;

/// <summary>A storage write operation ended.</summary>
/// <param name="Operation">The operation id.</param>
/// <param name="Result">How it ended.</param>
[Signal("storage.operation.done")]
public sealed record OperationDone(OperationId Operation, OperationResult Result) : Signal;
