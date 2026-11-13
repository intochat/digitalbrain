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

    /// <summary>Child flows running in parallel to be joined before this flow continues.</summary>
    Children,
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
/// <param name="ConfirmationPlanId">
/// The confirmation plan of an <see cref="WaitReason.Approval"/> wait, which the base sets; on an <see cref="WaitReason.External"/> wait a
/// caller-supplied plan id is kept as given (the flow does not resolve it); null on any other wait.
/// </param>
/// <param name="Children">Child flow ids this step waits for when reason is <see cref="WaitReason.Children"/>.</param>
/// <param name="IsRepeat">Whether this wait is a delay before repeating the current step.</param>
public sealed record Waiting(
    string Step,
    WaitReason Reason,
    DateTimeOffset? Expires,
    string? ConfirmationPlanId,
    IReadOnlyList<string>? Children = null,
    bool IsRepeat = false);

/// <summary>How a step execution result is handled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepDisposition>))]
public enum StepDisposition
{
    /// <summary>The step completed normally.</summary>
    Complete,

    /// <summary>The step was skipped.</summary>
    Skip,

    /// <summary>The step should be repeated.</summary>
    Repeat,

    /// <summary>The step is waiting on an approval, schedule, external signal or child flows.</summary>
    Wait,
}

/// <summary>What a step body returns.</summary>
/// <typeparam name="T">The flow's own state.</typeparam>
/// <param name="Next">The flow's own state after the step.</param>
/// <param name="Wait">Set when the step waits; the step completes when the wait is approved or arrives.</param>
/// <param name="Emits">Signals saved with the step's completion.</param>
/// <param name="Disposition">How the step outcome should be handled.</param>
/// <param name="Reason">Optional reason if the step is skipped or repeated.</param>
/// <param name="Delay">Optional delay if repeating the step.</param>
/// <param name="ChildRunIds">Child run ids if waiting for children.</param>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Fluent step result factory methods per kernel API design.")]
public sealed record StepResult<T>(
    T Next,
    Waiting? Wait = null,
    IReadOnlyList<Signal>? Emits = null,
    StepDisposition Disposition = StepDisposition.Complete,
    string? Reason = null,
    TimeSpan? Delay = null,
    IReadOnlyList<string>? ChildRunIds = null)
{
    /// <summary>Creates a step result that completes normally.</summary>
    public static StepResult<T> Complete(T next, IReadOnlyList<Signal>? emits = null) =>
        new(next, null, emits, StepDisposition.Complete);

    /// <summary>Creates a step result that skips the current step.</summary>
    public static StepResult<T> Skip(T next, string? reason = null) =>
        new(next, null, null, StepDisposition.Skip, Reason: reason);

    /// <summary>Creates a step result that repeats the current step after an optional delay.</summary>
    public static StepResult<T> Repeat(T next, TimeSpan? delay = null) =>
        new(next, null, null, StepDisposition.Repeat, Delay: delay);

    /// <summary>Creates a step result that pauses the flow until the specified child runs have finished.</summary>
    public static StepResult<T> WaitForChildren(T next, IReadOnlyList<string> childRunIds)
    {
        ArgumentNullException.ThrowIfNull(childRunIds);
        return new(next, new Waiting(string.Empty, WaitReason.Children, null, null, Children: childRunIds), null, StepDisposition.Wait, ChildRunIds: childRunIds);
    }
}

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

    /// <summary>How many times the body of <see cref="Current"/> was interrupted; reset when a step completes.</summary>
    public int Interruptions { get; init; }

    /// <summary>True when the flow has reached a terminal status.</summary>
    [JsonIgnore]
    public bool IsOver => Status is FlowStatus.Completed or FlowStatus.Failed or FlowStatus.Cancelled;

    /// <summary>True when the flow is running and not waiting on anything.</summary>
    [JsonIgnore]
    public bool Advancing => Status == FlowStatus.Running && Waiting is null;

    private bool Runs(string step) => Advancing && Current == step;

    /// <summary>Starts the flow if pending.</summary>
    public FlowTransition<T>? Start(string title, string owner, string? parentFlowId, T inner, DateTimeOffset now, string? traceId, string type, string? flowId = null)
    {
        if (Status != FlowStatus.Pending) { return null; }
        var id = flowId ?? FlowId;
        var next = this with
        {
            FlowId = id,
            Title = title,
            Owner = owner,
            TraceId = traceId ?? Guid.NewGuid().ToString("N"),
            Status = FlowStatus.Running,
            ParentFlowId = parentFlowId,
            Started = now,
            Inner = inner,
        };
        return new(next, [new FlowStarted(id, type, title)]);
    }

    /// <summary>Cancels the flow if not over.</summary>
    public FlowTransition<T>? RequestCancel(string reason, DateTimeOffset now)
        => IsOver ? null
            : new(this with { Status = FlowStatus.Cancelled, Waiting = null, Current = null, Ended = now },
                [new FlowDone(FlowId, FlowOutcome.Cancelled, reason)]);

    /// <summary>Starts the next step, or completes the flow if all steps are done.</summary>
    public FlowTransition<T>? StartStep(IReadOnlyList<string> steps, DateTimeOffset now)
    {
        if (!Advancing) { return null; }
        if (steps.FirstOrDefault(candidate => !Done.Contains(candidate)) is not { } step)
        {
            return new(this with { Status = FlowStatus.Completed, Current = null, Ended = now },
                [new FlowDone(FlowId, FlowOutcome.Completed, null)]);
        }
        return new(this with { Current = step }, [new FlowStep(FlowId, step, StepOutcome.Started, null)]);
    }

    /// <summary>Completes the running step with its result.</summary>
    public FlowTransition<T>? Complete(string step, StepResult<T> result)
        => Runs(step)
            ? new(this with { Done = Done.Add(step), Current = null, Inner = result.Next, Interruptions = 0 },
                [new FlowStep(FlowId, step, StepOutcome.Completed, null), .. result.Emits ?? []])
            : null;

    /// <summary>Skips the running step with its result.</summary>
    public FlowTransition<T>? Skip(string step, StepResult<T> result, string? reason = null)
        => Runs(step)
            ? new(this with { Done = Done.Add(step), Current = null, Inner = result.Next, Interruptions = 0 },
                [new FlowStep(FlowId, step, StepOutcome.Skipped, reason), .. result.Emits ?? []])
            : null;

    /// <summary>Repeats the running step without marking it as done.</summary>
    public FlowTransition<T>? Repeat(string step, StepResult<T> result)
        => Runs(step)
            ? new(this with { Current = null, Inner = result.Next, Interruptions = 0 },
                [new FlowStep(FlowId, step, StepOutcome.Repeated, null), .. result.Emits ?? []])
            : null;

    /// <summary>Transitions the running step into a waiting state.</summary>
    public FlowTransition<T>? Wait(string step, StepResult<T> result, Waiting wait, string? planId)
        => Runs(step)
            ? new(this with { Status = FlowStatus.Waiting, Waiting = wait with { Step = step, ConfirmationPlanId = planId }, Current = step, Inner = result.Next },
                [new FlowWaiting(FlowId, step, wait.Reason, planId), .. result.Emits ?? []])
            : null;

    /// <summary>Fails the flow if not over.</summary>
    public FlowTransition<T>? Fail(string step, string error, DateTimeOffset now)
        => Fail(step, error, now, null);

    /// <summary>Fails the flow if not over with custom done detail.</summary>
    public FlowTransition<T>? Fail(string step, string error, DateTimeOffset now, string? doneDetail)
        => IsOver ? null
            : new(this with { Status = FlowStatus.Failed, Waiting = null, Current = null, Ended = now },
                [new FlowStep(FlowId, step, StepOutcome.Failed, error), new FlowDone(FlowId, FlowOutcome.Failed, doneDetail ?? error)]);

    /// <summary>Records an interruption of the current step; fails the flow if max interruptions exceeded.</summary>
    public FlowTransition<T>? Interrupted(string step, string detail, DateTimeOffset now, int maxInterruptions = 20)
    {
        var count = Interruptions + 1;
        if (count >= maxInterruptions)
        {
            var failure = $"interrupted {maxInterruptions} times";
            return new(this with { Status = FlowStatus.Failed, Interruptions = count, Current = null, Ended = now },
                [new FlowStep(FlowId, step, StepOutcome.Failed, failure), new FlowDone(FlowId, FlowOutcome.Failed, failure)]);
        }
        return new(this with { Interruptions = count }, [new FlowStep(FlowId, step, StepOutcome.Interrupted, detail)]);
    }

    /// <summary>Resolves a waiting step.</summary>
    public FlowTransition<T>? Resolve(string step, WaitOutcome outcome, string? by, DateTimeOffset now)
    {
        if (Status != FlowStatus.Waiting || Waiting is not { } waiting || waiting.Step != step) { return null; }
        var resolve = new FlowResolve(FlowId, step, outcome, by);
        if (outcome is WaitOutcome.Approved or WaitOutcome.Arrived)
        {
            var nextDone = waiting.IsRepeat ? Done : Done.Add(step);
            return new(this with { Status = FlowStatus.Running, Waiting = null, Current = null, Done = nextDone, Interruptions = 0 },
                [resolve, new FlowStep(FlowId, step, waiting.IsRepeat ? StepOutcome.Repeated : StepOutcome.Completed, null)]);
        }
        return new(this with { Status = FlowStatus.Cancelled, Waiting = null, Current = null, Ended = now },
            [resolve, new FlowDone(FlowId, FlowOutcome.Cancelled, outcome.ToString())]);
    }

    /// <summary>Adds a relation to another flow.</summary>
    public FlowTransition<T>? Relate(FlowRelation relation)
        => Relations.Contains(relation) ? null
            : new(this with { Relations = Relations.Add(relation) }, [new FlowRelated(FlowId, relation)]);
}

/// <summary>The result of a pure state transition on a flow state.</summary>
/// <typeparam name="T">The flow's own state.</typeparam>
/// <param name="Next">The updated flow state.</param>
/// <param name="Signals">Signals emitted during the transition.</param>
public readonly record struct FlowTransition<T>(FlowState<T> Next, IReadOnlyList<Signal> Signals) where T : class, new();
