using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DigitalBrain.Flows;

/// <summary>Where a flow stands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlowStatus>))]
public enum FlowStatus
{
    /// <summary>Not started.</summary>
    Pending,

    /// <summary>Running its steps.</summary>
    Running,

    /// <summary>Waiting for an approval, an external signal or a time.</summary>
    Waiting,

    /// <summary>Held back by another flow.</summary>
    Blocked,

    /// <summary>Every step is done.</summary>
    Completed,

    /// <summary>A step failed.</summary>
    Failed,

    /// <summary>Cancelled, denied or expired.</summary>
    Cancelled,
}

/// <summary>What a waiting step waits for.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WaitReason>))]
public enum WaitReason
{
    /// <summary>A person's answer to a confirmation plan.</summary>
    Approval,

    /// <summary>A signal from outside the flow, delivered through <see cref="Flow{T}.Resume"/>.</summary>
    External,

    /// <summary>A time: when <see cref="Waiting.Expires"/> is reached the wait arrives and the flow goes on (or <see cref="Flow{T}.Resume"/> ends it earlier).</summary>
    Schedule,
}

/// <summary>How one flow relates to another.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RelationKind>))]
public enum RelationKind
{
    /// <summary>This flow waits for the target.</summary>
    BlockedBy,

    /// <summary>The target waits for this flow.</summary>
    Blocks,

    /// <summary>This flow runs after the target.</summary>
    Follows,

    /// <summary>This flow runs before the target.</summary>
    Precedes,

    /// <summary>The target is a subtask of this flow.</summary>
    Subtask,

    /// <summary>The target is this flow's parent.</summary>
    Parent,

    /// <summary>Loosely related.</summary>
    Relates,

    /// <summary>This flow duplicates the target.</summary>
    Duplicates,
}

/// <summary>A link from a flow to another flow.</summary>
/// <param name="TargetFlowId">The other flow's id.</param>
/// <param name="Kind">How they relate.</param>
/// <param name="Lag">The delay the relation asks for, if any.</param>
public sealed record FlowRelation(string TargetFlowId, RelationKind Kind, TimeSpan? Lag);

/// <summary>A step that waits before the flow goes on.</summary>
/// <param name="Step">The waiting step.</param>
/// <param name="Reason">What it waits for.</param>
/// <param name="Expires">When the wait ends as expired; null for never.</param>
/// <param name="ConfirmationPlanId">The confirmation plan of an <see cref="WaitReason.Approval"/> wait; the base sets it.</param>
public sealed record Waiting(string Step, WaitReason Reason, DateTimeOffset? Expires, string? ConfirmationPlanId);

/// <summary>What a step body returns.</summary>
/// <typeparam name="T">The flow's own state.</typeparam>
/// <param name="Next">The flow's own state after the step.</param>
/// <param name="Wait">Set when the step waits; the step completes when the wait is approved or arrives.</param>
/// <param name="Emits">Signals saved with the step's completion.</param>
public sealed record StepResult<T>(T Next, Waiting? Wait = null, IReadOnlyList<Signal>? Emits = null);

/// <summary>The persisted state of a <see cref="Flow{T}"/>. Persisted fields are only ever added.</summary>
/// <typeparam name="T">The flow's own state.</typeparam>
/// <param name="FlowId">The flow's neuron id.</param>
/// <param name="Title">The display title.</param>
/// <param name="Owner">Who the flow runs for.</param>
/// <param name="TraceId">The trace id current when the flow started.</param>
/// <param name="Status">Where the flow stands.</param>
/// <param name="Done">The completed steps, in order.</param>
/// <param name="Current">The step being run or waited on; null between steps.</param>
/// <param name="Waiting">The running wait, if any.</param>
/// <param name="Relations">Links to other flows.</param>
/// <param name="ParentFlowId">The parent flow's id, if any.</param>
/// <param name="Started">When the flow started.</param>
/// <param name="Ended">When the flow completed, failed or was cancelled.</param>
/// <param name="Inner">The flow's own state.</param>
public sealed record FlowState<T>(
    string FlowId,
    string Title,
    string Owner,
    string TraceId,
    FlowStatus Status,
    ImmutableList<string> Done,
    string? Current,
    Waiting? Waiting,
    ImmutableList<FlowRelation> Relations,
    string? ParentFlowId,
    DateTimeOffset Started,
    DateTimeOffset? Ended,
    T Inner) where T : class, new()
{
    /// <summary>The empty state of a flow that has not started.</summary>
    public FlowState()
        : this(string.Empty, string.Empty, string.Empty, string.Empty, FlowStatus.Pending, [], null, null, [], null, default, null, new T())
    {
    }
}
