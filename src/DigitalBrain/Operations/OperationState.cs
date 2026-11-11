using System.Text.Json.Serialization;
using DigitalBrain.Confirmation;
using DigitalBrain.State;

namespace DigitalBrain.Operations;

/// <summary>Where an operation is in its lifecycle.</summary>
public enum OperationPhase
{
    /// <summary>Started and actively executing.</summary>
    Running = 0,

    /// <summary>Ended with a terminal result.</summary>
    Done = 1,

    /// <summary>Waiting for approval before proceeding.</summary>
    AwaitingApproval = 2,
}

/// <summary>The result of a state transition: next state and published signals.</summary>
/// <param name="Next">The updated state.</param>
/// <param name="Signals">Signals emitted by this transition.</param>
public readonly record struct OperationTransition(OperationState Next, IReadOnlyList<Signal> Signals);

/// <summary>
/// The persisted state of one operation neuron (runtime.operation/id).
/// </summary>
[ExportableState]
public sealed record OperationState(
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
    string? DriverLifetime = null,
    string? TraceId = null,
    string? ApprovalPlanId = null,
    string? Guard = null,
    DateTimeOffset? Resumed = null)
{
    /// <summary>Creates an empty, uninitialized operation state.</summary>
    public OperationState()
        : this(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, null, OperationPhase.Running, false, 0d, null, default, default)
    {
    }

    /// <summary>True when this operation has an id.</summary>
    [JsonIgnore]
    public bool Exists => !string.IsNullOrEmpty(Id);

    /// <summary>The typed operation id key.</summary>
    [JsonIgnore]
    public OperationId Key => new(Id);

    /// <summary>Pure transition: places the operation into awaiting approval.</summary>
    public OperationTransition? AwaitApproval(string planId, string? guard, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        if (Phase != OperationPhase.Running || CancelRequested || ApprovalPlanId is not null)
        {
            return null;
        }

        var next = this with
        {
            Phase = OperationPhase.AwaitingApproval,
            ApprovalPlanId = planId,
            Guard = guard,
            Updated = now,
        };
        return new(next, [new OperationAwaitingApproval(Key, planId)]);
    }

    /// <summary>Pure transition: answers the pending approval.</summary>
    public OperationTransition? Answer(ConfirmationResult answer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (Phase != OperationPhase.AwaitingApproval || !string.Equals(answer.PlanId, ApprovalPlanId, StringComparison.Ordinal))
        {
            return null;
        }

        if (answer.Outcome == ConfirmationOutcome.Approved)
        {
            var next = this with
            {
                Phase = OperationPhase.Running,
                Resumed = now,
                Updated = now,
            };
            return new(next, [new OperationProgress(Key, 0d)]);
        }

        var (code, reason) = answer.Outcome switch
        {
            ConfirmationOutcome.Expired => ("expired", "The confirmation expired."),
            _ when answer.Reason == ConfirmationDenyReason.SelfApproval => ("declined", "Only a person can approve this."),
            _ => ("declined", "The confirmation was denied."),
        };

        return Complete(new OperationResult.Failed(code, reason), now);
    }

    /// <summary>Pure transition: requests cancellation.</summary>
    public OperationTransition? RequestCancel(bool handlerRuns, DateTimeOffset now)
    {
        if (Phase == OperationPhase.Done)
        {
            return null;
        }

        if (Phase == OperationPhase.Running && CancelRequested)
        {
            return null;
        }

        if (Phase == OperationPhase.Running && handlerRuns)
        {
            var next = this with
            {
                CancelRequested = true,
                Updated = now,
            };
            return new(next, []);
        }

        return Complete(new OperationResult.Cancelled(), now);
    }

    /// <summary>Pure transition: completes the operation with a result.</summary>
    public OperationTransition? Complete(OperationResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Exists || Phase == OperationPhase.Done)
        {
            return null;
        }

        var next = this with
        {
            Phase = OperationPhase.Done,
            Result = result,
            Fraction = result is OperationResult.Succeeded ? 1d : Fraction,
            Updated = now,
        };
        return new(next, [new OperationDone(Key, result)]);
    }

    /// <summary>Pure transition: advances progress fraction.</summary>
    public OperationTransition? Progress(double fraction, DateTimeOffset now)
    {
        if (Phase != OperationPhase.Running || CancelRequested)
        {
            return null;
        }

        var clamped = Math.Clamp(fraction, 0d, 1d);
        var next = this with
        {
            Fraction = clamped,
            Updated = now,
        };
        return new(next, [new OperationProgress(Key, clamped)]);
    }
}
