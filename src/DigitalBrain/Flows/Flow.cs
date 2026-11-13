using DigitalBrain.Modules;
using DigitalBrain.Units;
using System.Reflection;
using DigitalBrain.Brains;
using DigitalBrain.Confirmation;
using DigitalBrain.Contracts;
using DigitalBrain.Neurons;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Flows;

/// <summary>
/// One durable unit of work: declared <see cref="Steps"/> run in order through <see cref="Run"/>, each between two saves (the step
/// started, then the step done with the body's result). A body may ask to wait for an approval, an external signal or a time; the flow
/// then rests in <see cref="FlowStatus.Waiting"/> until <see cref="Resume"/>, the plan's <see cref="ConfirmationResolved"/> or the
/// <c>flow-expiry</c> reminder ends the wait. Bodies run at least once: a step interrupted mid-body (deactivation, crash) runs again
/// when the flow next activates or the <c>flow-nudge</c> reminder fires, so bodies must be idempotent: a duplicate activation
/// during a membership change can also run a step twice (the state's ETag stops the second save). A body that throws
/// <see cref="OperationCanceledException"/> (any token) is interrupted, not failed, and runs again the same way. On Orleans the grain
/// is not reentrant, so deactivation and shutdown cannot cancel a running body: shutdown waits for it or abandons it, and either way
/// the step runs again on activation. Each interruption is journaled as <see cref="StepOutcome.Interrupted"/> and counted in
/// <see cref="FlowState{T}.Interruptions"/>; the 20th interruption of the same step fails the flow. Every change journals one of the
/// <c>flow.*</c> signals.
/// <para>
/// A module hosting a flow must declare <c>Need.Reminders</c>: the <c>flow-nudge</c> and <c>flow-expiry</c> reminders are how it
/// recovers and how its waits expire. Bodies run at least once and must be idempotent.
/// </para>
/// <para>
/// A contract a flow implements that exposes <see cref="Cancel"/> must mark it <c>[Interleaves]</c>: a caller outside the host routes
/// by the contract, so without it a cancel waits behind the running body. Activation logs a Warning, once per type, when it is missing.
/// </para>
/// </summary>
/// <typeparam name="T">The flow's own state.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The activation token source holds no timer; OnDeactivate disposes it.")]
public abstract class Flow<T> : Neuron<FlowState<T>>, IFlowCallbacks where T : class, new()
{
    /// <summary>The reminder that expires an external or schedule wait.</summary>
    public const string ExpiryReminder = "flow-expiry";

    /// <summary>The reminder that advances a running flow that does not wait, so an interrupted or never-advanced flow recovers without outside traffic.</summary>
    public const string NudgeReminder = "flow-nudge";

    private static readonly TimeSpan NudgePeriod = TimeSpan.FromMinutes(1);

    // The interruptions of one step after which the flow fails rather than running it again.
    private const int MaxInterruptions = 20;

    // Approval waits expire through their plan; other waits through a reminder, re-checked every minute until they end.
    private static readonly TimeSpan ExpiryPeriod = TimeSpan.FromMinutes(1);

    // Cancelled when the flow is cancelled or the activation ends: the running body's token.
    private readonly CancellationTokenSource _activation = new();

    // The running body's own source, linked to _activation: Cancel fires only this one, so a cancel whose post fails interrupts the
    // current body and the next advance runs normally.
    private CancellationTokenSource? _running;

    // Completes when Cancel's post is sent (true) or failed (false), so an interrupted body knows whether the cancel holds.
    private Task<bool> _cancelPosted = Task.FromResult(true);

    // 1 once OnDeactivate disposed _activation: a late (interleaved) Cancel must not touch it.
    private int _disposed;

    // One-use proofs handed to the flow's own cancel post: a callback without one is a foreign caller.
    private readonly HashSet<string> _proofs = new(StringComparer.Ordinal);

    // Set by Cancel (which interleaves) until the Cancelled save, made in a turn of its own: nothing else saves meanwhile.
    private string? _cancelRequested;

    // The approval plan id currently asked by this activation.
    private string? _askedPlanId;

    /// <summary>The steps, in order. Step names are the keys of <see cref="FlowState{T}.Done"/> and never change.</summary>
    protected abstract IReadOnlyList<string> Steps { get; }

    /// <summary>Runs the body of <paramref name="step"/>. Throw <see cref="FlowFailed"/> to fail the flow.</summary>
    /// <param name="step">The step.</param>
    /// <param name="inner">The flow's own state.</param>
    /// <param name="ct">Fires when the flow is cancelled or the activation ends.</param>
    protected abstract Task<StepResult<T>> Run(string step, T inner, CancellationToken ct);

    private string FlowId => Id.ToString();

    private IConfirmationRequests ConfirmationRequests =>
        Seat.Services.GetService(typeof(IConfirmationRequests)) as IConfirmationRequests
        ?? Context.Get<IConfirmationRequests>(IConfirmationRequests.Of(Id.Brain));

    /// <summary>
    /// Starts the flow. A flow that already started ignores it. Returns once the start is durable (saved <see cref="FlowStatus.Running"/>
    /// with <see cref="FlowStarted"/>); steps run in later turns of this neuron, through a self-call deferred past this turn. A crash
    /// before the first step, a lost self-call or an interrupted step is recovered by the <c>flow-nudge</c> reminder started in this turn
    /// (every minute while the flow runs without waiting) or by the next activation, whichever comes first.
    /// </summary>
    /// <param name="title">The display title.</param>
    /// <param name="owner">Who the flow runs for.</param>
    /// <param name="parentFlowId">The parent flow's id, if any.</param>
    /// <param name="inner">The flow's own state.</param>
    public virtual async Task Start(string title, string owner, string? parentFlowId, T inner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(inner);
        var transition = State.Start(title, owner, parentFlowId, inner, Context.Time.GetUtcNow(), Context.TraceId, Id.Type, FlowId);
        if (transition is null) { return; }
        await Save(transition.Value.Next, transition.Value.Signals).ConfigureAwait(true);
        await StartNudge().ConfigureAwait(true);
        // A turn of its own, so the caller does not wait on the steps. A failed post is logged, not thrown: the start is saved and
        // the nudge or the next activation advances it.
        Context.Defer(async () =>
        {
            try { await Context.Get<IFlowCallbacks>(Id).Advance().ConfigureAwait(true); }
            catch (Exception error) when (error is not OutOfMemoryException) { FlowLog.AdvancePostFailed(Logger(), error, FlowId); }
        });
    }

    /// <inheritdoc />
    Task IFlowCallbacks.Advance() => State is { Status: FlowStatus.Running, Waiting: null } && _cancelRequested is null ? Advance() : Task.CompletedTask;

    /// <summary>
    /// Ends the wait of <paramref name="step"/>: <see cref="WaitOutcome.Approved"/> and <see cref="WaitOutcome.Arrived"/> complete the
    /// step and run on; any other outcome cancels the flow. Ignored unless the flow waits on <paramref name="step"/>.
    /// </summary>
    /// <param name="step">The waiting step.</param>
    /// <param name="outcome">How the wait ended.</param>
    /// <param name="by">Who ended it, if known.</param>
    /// <exception cref="InvalidOperationException">The flow waits on an approval: a person approves through the confirmations inbox only.</exception>
    public Task Resume(string step, WaitOutcome outcome, string? by)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step);
        if (State.Waiting is { Reason: WaitReason.Approval }) { throw new InvalidOperationException("approval waits are resolved by the confirmations inbox"); }
        return Resolve(step, outcome, by);
    }

    /// <summary>
    /// Cancels the flow: the running body's token fires, the flow ends <see cref="FlowOutcome.Cancelled"/> and a pending approval
    /// plan is answered deny. Ignored once the flow has ended or a cancel is under way. Cancelling the operations a flow started is
    /// the caller's job. This call interleaves with a running body, so it only fires the token and posts a self-call; the Cancelled
    /// save runs in a turn of its own once the body has stopped, and a step's result that arrives meanwhile is dropped.
    /// </summary>
    /// <remarks>A contract that exposes this method must mark it <c>[Interleaves]</c> as well, or a cancel from outside the host waits behind the body.</remarks>
    /// <param name="reason">Why.</param>
    [Interleaves]
    public async Task Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (State.IsOver || _cancelRequested is not null) { return; }
        _cancelRequested = reason;
        var posted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cancelPosted = posted.Task;
        await CancelRunning().ConfigureAwait(true);
        var proof = Proof();
        try
        {
            await Context.Get<IFlowCallbacks>(Id).CancelRequested(reason, proof).ConfigureAwait(true);
            posted.TrySetResult(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            posted.TrySetResult(false);
            // Not posted: the body was only interrupted, the nudge runs it again, and a later Cancel tries again.
            FlowLog.CancelPostFailed(Logger(), error, FlowId);
            Forget(proof);
            _cancelRequested = null;
            throw;
        }
    }

    /// <inheritdoc />
    async Task IFlowCallbacks.CancelRequested(string reason, string proof)
    {
        try { Prove(proof); }
        catch (InvalidOperationException error)
        {
            FlowLog.ForeignCancelRefused(Logger(), error, FlowId);
            throw;
        }
        if (State.IsOver) { return; }
        var waiting = State.Waiting;
        var transition = State.RequestCancel(reason, Context.Time.GetUtcNow());
        if (transition is not null)
        {
            await Save(transition.Value.Next, transition.Value.Signals).ConfigureAwait(true);
        }
        await StopNudge().ConfigureAwait(true);
        if (waiting is { Reason: WaitReason.Approval, ConfirmationPlanId: { } planId })
        {
            var requests = ConfirmationRequests;
            await requests.Withdraw(planId).ConfigureAwait(true);
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
        var transition = State.Relate(relation);
        if (transition is null) { return; }
        await Save(transition.Value.Next, transition.Value.Signals).ConfigureAwait(true);
    }

    /// <summary>
    /// Checks an approval wait when its plan is reported resolved; other plans are ignored.
    /// Resolves the authoritative answer directly through <see cref="IConfirmationRequests.Outcome"/>.
    /// </summary>
    /// <param name="resolved">The resolution.</param>
    [On]
    protected async Task OnConfirmation(ConfirmationResolved resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        if (State.Waiting is not { Reason: WaitReason.Approval, ConfirmationPlanId: { } planId } waiting || planId != resolved.PlanId)
        {
            return;
        }
        var requests = ConfirmationRequests;
        var outcome = await requests.Outcome(planId).ConfigureAwait(true);
        if (outcome is { } answer)
        {
            var approver = answer.Approver?.Id ?? (answer.Outcome == resolved.Outcome ? resolved.Approver?.Id : null);
            await Resolve(waiting.Step, WaitOutcomeOf(answer.Outcome), approver).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Checks a children wait when a child flow finishes; other signals are ignored.
    /// </summary>
    [On]
    protected async Task OnFlowDone(FlowDone done)
    {
        ArgumentNullException.ThrowIfNull(done);
        if (State is not { Status: FlowStatus.Waiting, Waiting: { Reason: WaitReason.Children, Children: { } children } waiting })
        {
            return;
        }

        var matching = children.FirstOrDefault(c => string.Equals(c, done.FlowId, StringComparison.Ordinal)
            || done.FlowId.EndsWith("/" + c, StringComparison.Ordinal));
        if (matching is null) { return; }

        var remaining = children.Where(c => !string.Equals(c, matching, StringComparison.Ordinal) && !done.FlowId.EndsWith("/" + c, StringComparison.Ordinal)).ToList();
        if (remaining.Count == 0)
        {
            await Resolve(waiting.Step, WaitOutcome.Arrived, null).ConfigureAwait(true);
        }
        else
        {
            var nextWaiting = waiting with { Children = remaining };
            var nextState = State with { Waiting = nextWaiting };
            await Save(nextState, [new FlowStep(FlowId, waiting.Step, StepOutcome.Started, $"Child {matching} finished; {remaining.Count} remaining.")]).ConfigureAwait(true);
        }
    }

    /// <summary>The confirmation plan an approval wait of <paramref name="step"/> raises.</summary>
    /// <param name="step">The waiting step.</param>
    /// <param name="inner">The flow's own state after the step's body.</param>
    protected virtual ConfirmationPlan PlanFor(string step, T inner)
        => new(Guid.NewGuid().ToString("N"), ConfirmationAction.Other, Destructive: false, Title: State.Title, Body: step,
            ApproveLabel: "Approve", DenyLabel: "Deny", TargetLabel: FlowId)
        { RequiresPerson = true };

    /// <inheritdoc />
    // Advances a running flow that does not wait, including one started but never advanced (Current null, Done empty).
    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        await Context.Synapses.Ensure<ConfirmationResolved>(IConfirmationRequests.Of(Id.Brain)).ConfigureAwait(true);
        WarnOnNonInterleavingCancel();
        if (State.Status == FlowStatus.Running && State.Waiting is null) { await Advance().ConfigureAwait(true); }
        else if (State is { Status: FlowStatus.Waiting, Waiting.Reason: WaitReason.Approval })
        {
            await StartNudge().ConfigureAwait(true);
            await ReRaise().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    protected override async Task OnDeactivate(DeactivationCause cause, CancellationToken cancellationToken)
    {
        await CancelActivation().ConfigureAwait(true);
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _activation.Dispose(); }
    }

    private async Task CancelRunning()
    {
        if (_running is not { } running) { return; }
        try { await running.CancelAsync().ConfigureAwait(true); }
        catch (ObjectDisposedException) { } // the body ended meanwhile
    }

    private async Task CancelActivation()
    {
        if (Volatile.Read(ref _disposed) != 0) { return; }
        try { await _activation.CancelAsync().ConfigureAwait(true); }
        catch (ObjectDisposedException) { } // disposed between the check and the cancel: the activation has ended
    }

    // The running body's token; a cancelled one once the activation's source is disposed.
    private CancellationToken ActivationToken()
    {
        if (Volatile.Read(ref _disposed) != 0) { return new CancellationToken(canceled: true); }
        try { return _activation.Token; }
        catch (ObjectDisposedException) { return new CancellationToken(canceled: true); }
    }

    /// <inheritdoc />
    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        if (name == NudgeReminder)
        {
            if (State is { Status: FlowStatus.Running, Waiting: null }) { await Advance().ConfigureAwait(true); }
            else if (State is { Status: FlowStatus.Waiting, Waiting.Reason: WaitReason.Approval }) { await ReRaise().ConfigureAwait(true); }
            else { await StopNudge().ConfigureAwait(true); }
            return;
        }
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
        var token = ActivationToken();
        while (State is { Status: FlowStatus.Running, Waiting: null } && !token.IsCancellationRequested && _cancelRequested is null)
        {
            var stepTransition = State.StartStep(Steps, Context.Time.GetUtcNow());
            if (stepTransition is null) { return; }
            if (stepTransition.Value.Next.Status == FlowStatus.Completed)
            {
                await Save(stepTransition.Value.Next, stepTransition.Value.Signals).ConfigureAwait(true);
                await StopNudge().ConfigureAwait(true);
                return;
            }
            await Save(stepTransition.Value.Next, stepTransition.Value.Signals).ConfigureAwait(true);
            var step = stepTransition.Value.Next.Current!;
            if (_cancelRequested is not null) { return; }
            StepResult<T> result;
            using var run = CancellationTokenSource.CreateLinkedTokenSource(token);
            _running = run;
            try { result = await Run(step, State.Inner, run.Token).ConfigureAwait(true); }
            // Interrupted, not failed: a cancelled body (any token) or an ending activation leaves Current set and the flow
            // running, so the nudge or the next activation runs the step again.
            // A host that is stopping can refuse the body's calls before it cancels the activation: that is an interruption too.
            catch (Exception error) when (error is OperationCanceledException || run.IsCancellationRequested || Context.Interrupts(error))
            {
                _running = null;
                // A cancel whose post failed leaves only an interruption behind.
                if (_cancelRequested is not null && !await _cancelPosted.ConfigureAwait(true)) { _cancelRequested = null; }
                if (_cancelRequested is null && State.Status == FlowStatus.Running) { await Interrupted(step, error.Message).ConfigureAwait(true); }
                return;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (State.Status != FlowStatus.Running || _cancelRequested is not null) { return; }
                var failTransition = State.Fail(step, error.Message, Context.Time.GetUtcNow());
                if (failTransition is not null)
                {
                    await Save(failTransition.Value.Next, failTransition.Value.Signals).ConfigureAwait(true);
                }
                await StopNudge().ConfigureAwait(true);
                return;
            }
            _running = null;
            // Cancelled while the body ran: its result is dropped.
            if (State.Status != FlowStatus.Running || result is null || _cancelRequested is not null) { return; }

            if (result.Disposition == StepDisposition.Skip)
            {
                var skipTransition = State.Skip(step, result, result.Reason);
                if (skipTransition is not null)
                {
                    await Save(skipTransition.Value.Next, skipTransition.Value.Signals).ConfigureAwait(true);
                }
                continue;
            }

            if (result.Disposition == StepDisposition.Repeat)
            {
                if (result.Delay is { } delay && delay > TimeSpan.Zero)
                {
                    var now = Context.Time.GetUtcNow();
                    var expires = now + delay;
                    var repeatWait = new Waiting(step, WaitReason.Schedule, expires, null, IsRepeat: true);
                    var transition = State.Wait(step, result, repeatWait, null);
                    if (transition is not null)
                    {
                        await Save(transition.Value.Next, [new FlowStep(FlowId, step, StepOutcome.Repeated, null), .. transition.Value.Signals]).ConfigureAwait(true);
                        await Context.Reminders.Start(ExpiryReminder, delay, ExpiryPeriod).ConfigureAwait(true);
                    }
                    await StopNudge().ConfigureAwait(true);
                    return;
                }
                else
                {
                    var repeatTransition = State.Repeat(step, result);
                    if (repeatTransition is not null)
                    {
                        await Save(repeatTransition.Value.Next, repeatTransition.Value.Signals).ConfigureAwait(true);
                    }
                    continue;
                }
            }

            if (result.Wait is { } wait)
            {
                if (wait.Reason == WaitReason.Children && (wait.Children is null || wait.Children.Count == 0))
                {
                    var complete = State.Complete(step, result);
                    if (complete is not null)
                    {
                        await Save(complete.Value.Next, complete.Value.Signals).ConfigureAwait(true);
                    }
                    continue;
                }
                await Wait(step, result, wait).ConfigureAwait(true);
                return;
            }
            var completeTransition = State.Complete(step, result);
            if (completeTransition is not null)
            {
                await Save(completeTransition.Value.Next, completeTransition.Value.Signals).ConfigureAwait(true);
            }
        }
    }

    // Journals an interruption of step; the MaxInterruptions-th fails the flow. A save refused by a stopping host is logged: the step
    // stays current and runs again either way.
    private async Task Interrupted(string step, string detail)
    {
        try
        {
            var transition = State.Interrupted(step, detail, Context.Time.GetUtcNow(), MaxInterruptions);
            if (transition is not null)
            {
                await Save(transition.Value.Next, transition.Value.Signals).ConfigureAwait(true);
                if (transition.Value.Next.Status == FlowStatus.Failed)
                {
                    await StopNudge().ConfigureAwait(true);
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { FlowLog.InterruptionNotSaved(Logger(), error, FlowId, step); }
    }

    private async Task Wait(string step, StepResult<T> result, Waiting wait)
    {
        var now = Context.Time.GetUtcNow();
        ConfirmationPlan? plan = null;
        if (wait.Reason == WaitReason.Approval)
        {
            plan = PlanFor(step, result.Next);
            if (wait.Expires is { } planExpires) { plan = plan with { Expiry = Positive(planExpires - now) }; }
        }
        // An external wait keeps a caller-supplied plan id (a plan another neuron raised for this wait); an approval wait gets the base's.
        var planId = plan?.Id ?? (wait.Reason == WaitReason.External ? wait.ConfirmationPlanId : null);
        var transition = State.Wait(step, result, wait, planId);
        if (transition is null) { return; }
        await Save(transition.Value.Next, transition.Value.Signals).ConfigureAwait(true);

        // An approval wait keeps the nudge: a plan lost before the confirmations side registered it is raised again.
        if (plan is not null)
        {
            try
            {
                var requests = ConfirmationRequests;
                var immediate = await requests.Request(plan).ConfigureAwait(true);
                _askedPlanId = plan.Id;
                if (immediate is { } answer)
                {
                    await Resolve(step, WaitOutcomeOf(answer.Outcome), answer.Approver?.Id).ConfigureAwait(true);
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                var failTransition = State.Fail(step, "confirmation: " + error.Message, now, doneDetail: "confirmation");
                if (failTransition is not null)
                {
                    await Save(failTransition.Value.Next, failTransition.Value.Signals).ConfigureAwait(true);
                }
                await StopNudge().ConfigureAwait(true);
            }
            return;
        }

        await StopNudge().ConfigureAwait(true);
        if (wait.Reason == WaitReason.Children && wait.Children is { Count: > 0 } children)
        {
            foreach (var childId in children)
            {
                var neuronId = NeuronAddress.IsBrainId(childId.Split('/')[0])
                    ? NeuronId.Parse(childId)
                    : new NeuronId(Id.Brain, "flow", childId);
                await Context.Synapses.Ensure<FlowDone>(neuronId).ConfigureAwait(true);
            }
        }
        if (wait.Expires is { } expires) { await Context.Reminders.Start(ExpiryReminder, Positive(expires - now), ExpiryPeriod).ConfigureAwait(true); }
    }

    private async Task ReRaise()
    {
        if (State is not { Status: FlowStatus.Waiting, Waiting: { Reason: WaitReason.Approval, ConfirmationPlanId: { } planId } waiting }) { return; }
        if (string.Equals(_askedPlanId, planId, StringComparison.Ordinal)) { return; }
        var plan = PlanFor(waiting.Step, State.Inner) with { Id = planId };
        if (waiting.Expires is { } expires) { plan = plan with { Expiry = Positive(expires - Context.Time.GetUtcNow()) }; }
        try
        {
            var requests = ConfirmationRequests;
            var immediate = await requests.Request(plan).ConfigureAwait(true);
            _askedPlanId = planId;
            if (immediate is { } answer)
            {
                await Resolve(waiting.Step, WaitOutcomeOf(answer.Outcome), answer.Approver?.Id).ConfigureAwait(true);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var failTransition = State.Fail(waiting.Step, "confirmation: " + error.Message, Context.Time.GetUtcNow(), doneDetail: "confirmation");
            if (failTransition is not null)
            {
                await Save(failTransition.Value.Next, failTransition.Value.Signals).ConfigureAwait(true);
            }
            await StopNudge().ConfigureAwait(true);
        }
    }

    // A runtime without services (a test context) logs nowhere rather than turning a logged failure into a thrown one.
    private ILogger Logger()
    {
        try { return (Seat.Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger("DigitalBrain.Flows") ?? NullLogger.Instance; }
        catch (NotSupportedException) { return NullLogger.Instance; }
    }

    private async Task Resolve(string step, WaitOutcome outcome, string? by)
    {
        if (State is not { Status: FlowStatus.Waiting, Waiting: { } waiting } || waiting.Step != step || _cancelRequested is not null) { return; }
        _askedPlanId = null;
        var transition = State.Resolve(step, outcome, by, Context.Time.GetUtcNow());
        if (transition is null) { return; }
        await Save(transition.Value.Next, transition.Value.Signals).ConfigureAwait(true);
        if (outcome is WaitOutcome.Approved or WaitOutcome.Arrived)
        {
            await StartNudge().ConfigureAwait(true);
        }
        if (waiting is { Reason: not WaitReason.Approval, Expires: not null }) { await Context.Reminders.Stop(ExpiryReminder).ConfigureAwait(true); }
        await Advance().ConfigureAwait(true);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> CancelChecked = new();

    // A contract whose Cancel lacks [Interleaves] makes outside cancels wait behind the body: warn once per flow type.
    private void WarnOnNonInterleavingCancel()
    {
        if (!CancelChecked.TryAdd(GetType(), true)) { return; }
        foreach (var contract in GetType().GetInterfaces())
        {
            if (contract == typeof(IFlowCallbacks)) { continue; }
            if (contract.GetMethod(nameof(Cancel), [typeof(string)]) is { } cancel && !cancel.IsDefined(typeof(InterleavesAttribute), false))
            {
                FlowLog.CancelNotInterleaved(Logger(), GetType().FullName ?? GetType().Name, contract.FullName ?? contract.Name);
            }
        }
    }

    private string Proof()
    {
        var proof = Guid.NewGuid().ToString("N");
        lock (_proofs) { _proofs.Add(proof); }
        return proof;
    }

    private void Forget(string proof)
    {
        lock (_proofs) { _proofs.Remove(proof); }
    }

    // Spends a proof; a call without a live one is not the flow's own cancel post.
    private void Prove(string? proof)
    {
        bool known;
        lock (_proofs) { known = proof is not null && _proofs.Remove(proof); }
        if (!known) { throw new InvalidOperationException("only the flow's own detached work calls back on it"); }
    }

    private Task StartNudge() => Context.Reminders.Start(NudgeReminder, NudgePeriod, NudgePeriod);

    private Task StopNudge() => Context.Reminders.Stop(NudgeReminder);

    private static WaitOutcome WaitOutcomeOf(ConfirmationOutcome outcome) => outcome switch
    {
        ConfirmationOutcome.Approved => WaitOutcome.Approved,
        ConfirmationOutcome.Expired => WaitOutcome.Expired,
        _ => WaitOutcome.Denied,
    };

    private static TimeSpan Positive(TimeSpan span) => span > TimeSpan.Zero ? span : TimeSpan.FromSeconds(1);
}

/// <summary>
/// What a flow's own detached work calls back on it, so results are saved in a turn. Not for other callers: each call but
/// <see cref="Advance"/> carries a one-use proof the flow handed its detached work, and a call without a live one throws
/// <see cref="InvalidOperationException"/>.
/// </summary>
public interface IFlowCallbacks
{
    /// <summary>A cancel the flow's <see cref="Flow{T}.Cancel"/> posted: saves the flow Cancelled in a turn, after the running body stopped.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="proof">The proof the flow handed its own cancel.</param>
    [OneWayCall]
    Task CancelRequested(string reason, string proof);

    /// <summary>Runs the steps of a started flow that is running and not waiting; otherwise ignored. One-way: the self-call Start defers must not wait on its own turn.</summary>
    [OneWayCall]
    Task Advance();
}

internal static partial class FlowLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Flow {FlowType} implements {Contract}.Cancel without [Interleaves]; a cancel from outside the host waits behind the running body")]
    public static partial void CancelNotInterleaved(ILogger logger, string flowType, string contract);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flow {FlowId} refused a CancelRequested without a live proof")]
    public static partial void ForeignCancelRefused(ILogger logger, Exception exception, string flowId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flow {FlowId} could not post its cancel; the running body was interrupted and the flow runs on")]
    public static partial void CancelPostFailed(ILogger logger, Exception exception, string flowId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flow {FlowId} could not journal the interruption of step {Step}; it runs again")]
    public static partial void InterruptionNotSaved(ILogger logger, Exception exception, string flowId, string step);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Flow {FlowId} could not post its first advance; the flow-nudge reminder or the next activation advances it")]
    public static partial void AdvancePostFailed(ILogger logger, Exception exception, string flowId);
}
