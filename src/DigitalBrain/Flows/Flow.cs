using System.Reflection;
using DigitalBrain.Confirmation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Flows;

/// <summary>
/// One durable unit of work: declared <see cref="Steps"/> run in order through <see cref="Run"/>, each between two saves (the step
/// started, then the step done with the body's result). A body may ask to wait for an approval, an external signal or a time; the flow
/// then rests in <see cref="FlowStatus.Waiting"/> until <see cref="Resume"/>, the plan's <see cref="ConfirmationResolved"/> or the
/// <c>flow-expiry</c> reminder ends the wait. Bodies run at least once: a step interrupted mid-body (deactivation, crash) runs again,
/// once, when the flow next activates, so bodies are expected to be idempotent. Every change journals one of the <c>flow.*</c> signals.
/// </summary>
/// <typeparam name="T">The flow's own state.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The activation token source holds no timer; OnDeactivate disposes it.")]
public abstract class Flow<T> : Neuron<FlowState<T>>, IFlowCallbacks where T : class, new()
{
    /// <summary>The reminder that expires an external or schedule wait.</summary>
    public const string ExpiryReminder = "flow-expiry";

    // Approval waits expire through their plan; other waits through a reminder, re-checked every minute until they end.
    private static readonly TimeSpan ExpiryPeriod = TimeSpan.FromMinutes(1);

    // Cancelled when the flow is cancelled or the activation ends: the running body's token.
    private readonly CancellationTokenSource _activation = new();

    /// <summary>The steps, in order. Step names are the keys of <see cref="FlowState{T}.Done"/> and never change.</summary>
    protected abstract IReadOnlyList<string> Steps { get; }

    /// <summary>Runs the body of <paramref name="step"/>. Throw <see cref="FlowFailed"/> to fail the flow.</summary>
    /// <param name="step">The step.</param>
    /// <param name="inner">The flow's own state.</param>
    /// <param name="ct">Fires when the flow is cancelled or the activation ends.</param>
    protected abstract Task<StepResult<T>> Run(string step, T inner, CancellationToken ct);

    private string FlowId => Id.ToString();

    /// <summary>Starts the flow and runs its steps until it waits or ends. A flow that already started ignores it.</summary>
    /// <param name="title">The display title.</param>
    /// <param name="owner">Who the flow runs for.</param>
    /// <param name="parentFlowId">The parent flow's id, if any.</param>
    /// <param name="inner">The flow's own state.</param>
    public virtual async Task Start(string title, string owner, string? parentFlowId, T inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(inner);
        if (State.Status != FlowStatus.Pending) { return; }
        var next = State with
        {
            FlowId = FlowId,
            Title = title,
            Owner = owner,
            TraceId = FlowTrace.Current() ?? Guid.NewGuid().ToString("N"),
            Status = FlowStatus.Running,
            ParentFlowId = parentFlowId,
            Started = Context.Time.GetUtcNow(),
            Inner = inner,
        };
        await Save(next, new FlowStarted(FlowId, Id.Type, title)).ConfigureAwait(true);
        await Advance().ConfigureAwait(true);
    }

    /// <summary>
    /// Ends the wait of <paramref name="step"/>: <see cref="WaitOutcome.Approved"/> and <see cref="WaitOutcome.Arrived"/> complete the
    /// step and run on; any other outcome cancels the flow. Ignored unless the flow waits on <paramref name="step"/>.
    /// </summary>
    /// <param name="step">The waiting step.</param>
    /// <param name="outcome">How the wait ended.</param>
    /// <param name="by">Who ended it, if known.</param>
    public Task Resume(string step, WaitOutcome outcome, string? by)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        return Resolve(step, outcome, by);
    }

    /// <summary>
    /// Cancels the flow: the running body's token fires, the flow ends <see cref="FlowOutcome.Cancelled"/> and a pending approval
    /// plan is answered deny. Ignored once the flow has ended. Cancelling the operations a flow started is the caller's job.
    /// </summary>
    /// <param name="reason">Why.</param>
    [Interleaves]
    public async Task Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Ended(State.Status)) { return; }
        var waiting = State.Waiting;
        await Save(State with { Status = FlowStatus.Cancelled, Waiting = null, Current = null, Ended = Context.Time.GetUtcNow() },
            new FlowDone(FlowId, FlowOutcome.Cancelled, reason)).ConfigureAwait(true);
        await _activation.CancelAsync().ConfigureAwait(true);
        if (waiting is { Reason: WaitReason.Approval, ConfirmationPlanId: { } planId })
        {
            await Context.Confirmations.Resolve(planId, approve: false).ConfigureAwait(true);
        }
        else if (waiting is { Expires: not null })
        {
            await Context.Reminders.Stop(ExpiryReminder).ConfigureAwait(true);
        }
    }

    /// <summary>Adds <paramref name="relation"/> to the flow's relations; an existing one is ignored.</summary>
    /// <param name="relation">The relation.</param>
    public async Task Relate(FlowRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        if (State.Relations.Contains(relation)) { return; }
        await Save(State with { Relations = State.Relations.Add(relation) }, new FlowRelated(FlowId, relation)).ConfigureAwait(true);
    }

    /// <summary>Ends an approval wait when its plan is resolved; other plans are ignored.</summary>
    /// <param name="resolved">The resolution.</param>
    [On]
    protected Task OnConfirmation(ConfirmationResolved resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (State.Waiting is not { Reason: WaitReason.Approval, ConfirmationPlanId: { } planId } waiting || planId != resolved.PlanId)
        {
            return Task.CompletedTask;
        }
        return Resolve(waiting.Step, WaitOutcomeOf(resolved.Outcome), resolved.Approver?.Id);
    }

    /// <inheritdoc />
    async Task IFlowCallbacks.ConfirmationFailed(string planId, string detail)
    {
        if (State is not { Status: FlowStatus.Waiting, Waiting: { Reason: WaitReason.Approval } waiting } || waiting.ConfirmationPlanId != planId) { return; }
        await Save(State with { Status = FlowStatus.Failed, Waiting = null, Ended = Context.Time.GetUtcNow() },
            [new FlowStep(FlowId, waiting.Step, StepOutcome.Failed, "confirmation: " + detail), new FlowDone(FlowId, FlowOutcome.Failed, "confirmation")]).ConfigureAwait(true);
    }

    /// <inheritdoc />
    Task IFlowCallbacks.ConfirmationAnswered(string planId, ConfirmationOutcome outcome)
    {
        if (State is not { Status: FlowStatus.Waiting, Waiting: { Reason: WaitReason.Approval } waiting } || waiting.ConfirmationPlanId != planId) { return Task.CompletedTask; }
        return Resolve(waiting.Step, WaitOutcomeOf(outcome), null);
    }

    /// <summary>The confirmation plan an approval wait of <paramref name="step"/> raises.</summary>
    /// <param name="step">The waiting step.</param>
    /// <param name="inner">The flow's own state after the step's body.</param>
    protected virtual ConfirmationPlan PlanFor(string step, T inner)
        => new(Guid.NewGuid().ToString("N"), ConfirmationAction.Other, Destructive: false, Title: State.Title, Body: step,
            ApproveLabel: "Approve", DenyLabel: "Deny", TargetLabel: FlowId)
        { RequiresPerson = true };

    /// <inheritdoc />
    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        await Context.Synapses.EnsureBrainWide(typeof(ConfirmationResolved)).ConfigureAwait(true);
        if (State.Status == FlowStatus.Running && State.Waiting is null) { await Advance().ConfigureAwait(true); }
    }

    /// <inheritdoc />
    protected override async Task OnDeactivate(DeactivationCause cause, CancellationToken cancellationToken)
    {
        await _activation.CancelAsync().ConfigureAwait(true);
        _activation.Dispose();
    }

    /// <inheritdoc />
    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        if (name != ExpiryReminder) { return; }
        if (State is { Status: FlowStatus.Waiting, Waiting: { Reason: not WaitReason.Approval, Expires: { } expires } waiting })
        {
            if (Context.Time.GetUtcNow() >= expires) { await Resolve(waiting.Step, waiting.Reason == WaitReason.Schedule ? WaitOutcome.Arrived : WaitOutcome.Expired, null).ConfigureAwait(true); }
            return;
        }
        await Context.Reminders.Stop(ExpiryReminder).ConfigureAwait(true);
    }

    // Runs the first step not done until the flow waits or ends.
    private async Task Advance()
    {
        var token = _activation.Token;
        while (State is { Status: FlowStatus.Running, Waiting: null } && !token.IsCancellationRequested)
        {
            if (Steps.FirstOrDefault(candidate => !State.Done.Contains(candidate)) is not { } step)
            {
                await Save(State with { Status = FlowStatus.Completed, Current = null, Ended = Context.Time.GetUtcNow() },
                    new FlowDone(FlowId, FlowOutcome.Completed, null)).ConfigureAwait(true);
                return;
            }
            await Save(State with { Current = step }, new FlowStep(FlowId, step, StepOutcome.Started, null)).ConfigureAwait(true);
            StepResult<T> result;
            try { result = await Run(step, State.Inner, token).ConfigureAwait(true); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (State.Status != FlowStatus.Running) { return; }
                await Save(State with { Status = FlowStatus.Failed, Ended = Context.Time.GetUtcNow() },
                    [new FlowStep(FlowId, step, StepOutcome.Failed, error.Message), new FlowDone(FlowId, FlowOutcome.Failed, error.Message)]).ConfigureAwait(true);
                return;
            }
            // Cancelled while the body ran: its result is dropped.
            if (State.Status != FlowStatus.Running || result is null) { return; }
            if (result.Wait is { } wait)
            {
                await Wait(step, result, wait).ConfigureAwait(true);
                return;
            }
            await Save(State with { Done = State.Done.Add(step), Current = null, Inner = result.Next },
                [new FlowStep(FlowId, step, StepOutcome.Completed, null), .. result.Emits ?? []]).ConfigureAwait(true);
        }
    }

    private async Task Wait(string step, StepResult<T> result, Waiting wait)
    {
        var now = Context.Time.GetUtcNow();
        ConfirmationPlan? plan = null;
        if (wait.Reason == WaitReason.Approval)
        {
            plan = PlanFor(step, result.Next);
            if (wait.Expires is { } expires) { plan = plan with { Expiry = Positive(expires - now) }; }
        }
        var waiting = wait with { Step = step, ConfirmationPlanId = plan?.Id };
        await Save(State with { Status = FlowStatus.Waiting, Waiting = waiting, Current = step, Inner = result.Next },
            [new FlowWaiting(FlowId, step, wait.Reason, plan?.Id), .. result.Emits ?? []]).ConfigureAwait(true);
        if (plan is not null) { _ = Ask(plan); }
        else if (wait.Expires is { } expires) { await Context.Reminders.Start(ExpiryReminder, Positive(expires - now), ExpiryPeriod).ConfigureAwait(true); }
    }

    // Raises the plan without awaiting the answer inside the turn; the answer arrives as ConfirmationResolved.
    // Results go back through the neuron's own mailbox (a self-call), so they are saved in a turn, never from this detached task.
    private async Task Ask(ConfirmationPlan plan)
    {
        Func<IFlowCallbacks, Task> relay;
        try
        {
            var result = await Context.Confirm(plan, CancellationToken.None).ConfigureAwait(true);
            relay = self => self.ConfirmationAnswered(plan.Id, result.Outcome);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var detail = error.Message;
            relay = self => self.ConfirmationFailed(plan.Id, detail);
        }
        try { await relay(Context.Get<IFlowCallbacks>(Id)).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The flow stays waiting on the plan; the log is the only trace (IConfirmations has no read of pending plans).
            FlowLog.RelayFailed(Logger(), error, FlowId, plan.Id);
        }
    }

    private ILogger Logger()
        => (Context.Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger("DigitalBrain.Flows") ?? NullLogger.Instance;

    private async Task Resolve(string step, WaitOutcome outcome, string? by)
    {
        if (State is not { Status: FlowStatus.Waiting, Waiting: { } waiting } || waiting.Step != step) { return; }
        var resolve = new FlowResolve(FlowId, step, outcome, by);
        if (outcome is WaitOutcome.Approved or WaitOutcome.Arrived)
        {
            await Save(State with { Status = FlowStatus.Running, Waiting = null, Current = null, Done = State.Done.Add(step) },
                [resolve, new FlowStep(FlowId, step, StepOutcome.Completed, null)]).ConfigureAwait(true);
        }
        else
        {
            await Save(State with { Status = FlowStatus.Cancelled, Waiting = null, Current = null, Ended = Context.Time.GetUtcNow() },
                [resolve, new FlowDone(FlowId, FlowOutcome.Cancelled, outcome.ToString())]).ConfigureAwait(true);
        }
        if (waiting is { Reason: not WaitReason.Approval, Expires: not null }) { await Context.Reminders.Stop(ExpiryReminder).ConfigureAwait(true); }
        await Advance().ConfigureAwait(true);
    }

    private static WaitOutcome WaitOutcomeOf(ConfirmationOutcome outcome) => outcome switch
    {
        ConfirmationOutcome.Approved => WaitOutcome.Approved,
        ConfirmationOutcome.Expired => WaitOutcome.Expired,
        _ => WaitOutcome.Denied,
    };

    private static bool Ended(FlowStatus status) => status is FlowStatus.Completed or FlowStatus.Failed or FlowStatus.Cancelled;

    private static TimeSpan Positive(TimeSpan span) => span > TimeSpan.Zero ? span : TimeSpan.FromSeconds(1);
}

/// <summary>What a flow's own detached work calls back on it, so results are saved in a turn. Not for other callers.</summary>
public interface IFlowCallbacks
{
    /// <summary>The confirmation plan <paramref name="planId"/> could not be raised; the waiting flow fails.</summary>
    /// <param name="planId">The plan id.</param>
    /// <param name="detail">Why.</param>
    Task ConfirmationFailed(string planId, string detail);

    /// <summary>
    /// <see cref="IConfirmations.Confirm"/> returned <paramref name="outcome"/> for <paramref name="planId"/>. It ends the wait like the
    /// plan's <see cref="ConfirmationResolved"/> (whichever arrives first wins), so a result that comes back without that signal
    /// (skipped by policy, refused up front) still resumes the flow.
    /// </summary>
    /// <param name="planId">The plan id.</param>
    /// <param name="outcome">How it ended.</param>
    Task ConfirmationAnswered(string planId, ConfirmationOutcome outcome);
}

internal static partial class FlowLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Flow {FlowId} could not record the answer to confirmation plan {PlanId}; it stays waiting")]
    public static partial void RelayFailed(ILogger logger, Exception exception, string flowId, string planId);
}

// The request's trace id. TraceContext lives in DigitalBrain.Capabilities.Compute, which this assembly does not reference, so it is
// read by reflection when that assembly is loaded (as TestOS's TestCause does).
internal static class FlowTrace
{
    private static readonly Lazy<PropertyInfo?> TraceCurrent = new(static () => AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(assembly => assembly.GetName().Name == "DigitalBrain.Capabilities.Compute")
        ?.GetType("DigitalBrain.Capabilities.Compute.TraceContext")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static));

    public static string? Current()
    {
        var trace = TraceCurrent.Value?.GetValue(null);
        return trace?.GetType().GetProperty("TraceId")?.GetValue(trace) as string;
    }
}
