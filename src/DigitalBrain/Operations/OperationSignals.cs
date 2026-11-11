namespace DigitalBrain.Operations;

/// <summary>An operation started.</summary>
/// <param name="Operation">The operation id.</param>
/// <param name="Kind">The plan type name (for example <c>WritePlan</c>).</param>
/// <param name="Origin">The neuron or app that started it.</param>
[Signal("storage.operation.started")]
public sealed record OperationStarted(OperationId Operation, string Kind, string Origin) : Signal;

/// <summary>An operation is waiting for a person's approval.</summary>
/// <param name="Operation">The operation id.</param>
/// <param name="PlanId">The confirmation plan id.</param>
[Signal("operation.awaiting")]
public sealed record OperationAwaitingApproval(OperationId Operation, string PlanId) : Signal;

/// <summary>An operation reports progress.</summary>
/// <param name="Operation">The operation id.</param>
/// <param name="Fraction">The fraction done, 0..1.</param>
[Signal("storage.operation.progress")]
public sealed record OperationProgress(OperationId Operation, double Fraction) : Signal;

/// <summary>An operation ended.</summary>
/// <param name="Operation">The operation id.</param>
/// <param name="Result">How it ended.</param>
[Signal("storage.operation.done")]
public sealed record OperationDone(OperationId Operation, OperationResult Result) : Signal;
