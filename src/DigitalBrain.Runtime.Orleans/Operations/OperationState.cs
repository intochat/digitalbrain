using DigitalBrain.Operations;

namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>Where an operation is.</summary>
internal enum OperationPhase
{
    /// <summary>Started and not yet ended.</summary>
    Running,

    /// <summary>Ended; <see cref="OperationState.Result"/> says how. A done operation never runs again.</summary>
    Done,
}

/// <summary>
/// The persisted state of one operation entity <c>brain/runtime.operation/id</c> (D-04), JSON under the state name
/// <c>operation</c>. <see cref="PlanJson"/> never carries inline file bytes: only their length and SHA-256 (T-05-11-05).
/// </summary>
/// <param name="Id">The operation id, also the entity key.</param>
/// <param name="Brain">The brain the operation runs in.</param>
/// <param name="Origin">The neuron (<c>brain/type/key</c>) or app that started it (audit seed for CAP-05).</param>
/// <param name="Kind">The plan type, for example <c>WritePlan</c>.</param>
/// <param name="PlanJson">The plan as JSON, without inline bytes.</param>
/// <param name="InlineLength">The length of inline content the plan carried, if any.</param>
/// <param name="InlineHash">The SHA-256 of that inline content (<c>sha256:hex</c>), if any.</param>
/// <param name="Phase">Running or done.</param>
/// <param name="CancelRequested">Cancel was asked for; the runner stops at its next step and further progress is refused.</param>
/// <param name="Fraction">The last saved progress, 0..1.</param>
/// <param name="Result">How it ended, once done.</param>
/// <param name="Started">When it started.</param>
/// <param name="Updated">When the state last changed.</param>
/// <param name="Driver">The exact silo address and generation running the operation, absent for older saved operations.</param>
/// <param name="DriverLifetime">Optional host evidence identifying the exact process running the operation.</param>
internal sealed record OperationState(
    string Id,
    string Brain,
    string Origin,
    string Kind,
    string PlanJson,
    long? InlineLength,
    string? InlineHash,
    OperationPhase Phase,
    bool CancelRequested,
    double Fraction,
    OperationResult? Result,
    DateTimeOffset Started,
    DateTimeOffset Updated,
    string? Driver = null,
    string? DriverLifetime = null);
