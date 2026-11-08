using DigitalBrain.Confirmation;
using DigitalBrain.UI;
using DigitalBrain.UI.Confirmation;

namespace DigitalBrain.Testing;

/// <summary>
/// The confirmation gate in tests (07 D-03, D-09, D-16, D-19): the brain <see cref="Policy"/>, pending plans keyed by id, expiry on
/// the given clock (advance it past the plan's expiry to expire it), the human-principal rule, and the audit of every
/// <see cref="ConfirmationResolved"/>. <see cref="For"/> gives a requester its <see cref="IConfirmations"/>; a requester calling
/// <see cref="IConfirmations.Resolve"/> on it answers as itself, so an app relaying an approval is denied
/// <see cref="ConfirmationDenyReason.SelfApproval"/>. <see cref="Approve"/>, <see cref="Deny"/> and <see cref="Click"/> answer as
/// <see cref="Current"/>, the principal looking at the screen.
/// </summary>
public sealed class TestConfirmations
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private readonly List<ConfirmationRequested> _requested = [];
    private readonly List<ConfirmationResolved> _audit = [];
    private bool _approveAll;

    /// <summary>Confirmations on <paramref name="clock"/>.</summary>
    public TestConfirmations(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>The brain policy. Default <see cref="ConfirmationPolicy.DestructiveOnly"/>.</summary>
    public ConfirmationPolicy Policy { get; set; } = ConfirmationPolicy.DestructiveOnly;

    /// <summary>The principal viewing the screen; <see cref="Approve"/>, <see cref="Deny"/> and <see cref="Click"/> answer as it. Default a person.</summary>
    public ConfirmationPrincipal Current { get; set; } = ConfirmationPrincipal.Person("person");

    /// <summary>Every plan put to a person, oldest first.</summary>
    public IReadOnlyList<ConfirmationRequested> Requested
    {
        get { lock (_gate) { return [.. _requested]; } }
    }

    /// <summary>The plans still waiting for an answer.</summary>
    public IReadOnlyList<ConfirmationRequested> Waiting
    {
        get { lock (_gate) { return [.. _pending.Values.Select(pending => pending.Request)]; } }
    }

    /// <summary>Every resolution, skipped ones included, oldest first: the audit record.</summary>
    public IReadOnlyList<ConfirmationResolved> Audit
    {
        get { lock (_gate) { return [.. _audit]; } }
    }

    /// <summary>The gate as <paramref name="requester"/> sees it; signals also go to <paramref name="publish"/> when given.</summary>
    public IConfirmations For(ConfirmationPrincipal requester, Func<Signal, Task>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return new Requester(this, requester, publish);
    }

    /// <summary>
    /// From now on approves every plan put to a person as <see cref="Current"/> as soon as it is asked; the plan is still
    /// <see cref="Requested"/> and audited. Plans the <see cref="Policy"/> skips stay skipped.
    /// </summary>
    public void ApproveAll() => Volatile.Write(ref _approveAll, true);

    /// <summary>Approves <paramref name="planId"/> as <see cref="Current"/>.</summary>
    public Task Approve(string planId) => Resolve(planId, approve: true, Current);

    /// <summary>Denies <paramref name="planId"/> as <see cref="Current"/>.</summary>
    public Task Deny(string planId) => Resolve(planId, approve: false, Current);

    /// <summary>Clicks a rendered confirmation button as <see cref="Current"/>, as the shell dispatches it to the confirmations neuron.</summary>
    /// <exception cref="ArgumentException">The button does not target <see cref="ConfirmView.ConfirmationsNeuronKey"/>/<see cref="ConfirmView.ResolveOperation"/>.</exception>
    /// <exception cref="InvalidOperationException">The button is disabled.</exception>
    public Task Click(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);
        var emit = button.Emit;
        if (emit is null || emit.Owner != IConfirmations.ConfirmationsNeuronKey || emit.Signal != IConfirmations.ResolveOperation
            || emit.Args is not { ValueKind: System.Text.Json.JsonValueKind.Object } args
            || !args.TryGetProperty("planId", out var planId) || !args.TryGetProperty("approve", out var approve))
        {
            throw new ArgumentException($"The button emits {emit?.Owner}/{emit?.Signal}, not the confirmations neuron.", nameof(button));
        }
        if (!button.Enabled) { throw new InvalidOperationException($"The button '{button.Label}' is disabled."); }
        return Resolve(planId.GetString()!, approve.GetBoolean(), Current);
    }

    /// <summary>Answers <paramref name="planId"/> as <paramref name="approver"/>. The first answer wins; unknown or answered ids are ignored.</summary>
    public async Task Resolve(string planId, bool approve, ConfirmationPrincipal approver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(approver);
        var (outcome, reason) = !approve ? (ConfirmationOutcome.Denied, ConfirmationDenyReason.ByPerson)
            : approver.IsHuman ? (ConfirmationOutcome.Approved, ConfirmationDenyReason.None)
            : (ConfirmationOutcome.Denied, ConfirmationDenyReason.SelfApproval);
        await End(planId, approver, outcome, reason).ConfigureAwait(false);
    }

    private async Task<ConfirmationResult> Confirm(ConfirmationPrincipal requester, Func<Signal, Task>? publish, ConfirmationPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.Id);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(plan.Expiry, TimeSpan.Zero);
        var policy = Policy;
        if (!ConfirmationRules.Asks(policy, plan))
        {
            var skipped = new ConfirmationResolved(plan.Id, plan.Action, plan.TargetLabel, requester, null, policy, ConfirmationOutcome.Approved,
                ConfirmationDenyReason.None, Skipped: true, _clock.GetUtcNow());
            lock (_gate) { _audit.Add(skipped); }
            if (publish is not null) { await publish(skipped).ConfigureAwait(false); }
            return new ConfirmationResult(plan.Id, ConfirmationOutcome.Approved, ConfirmationDenyReason.None, Skipped: true);
        }

        var request = new ConfirmationRequested(plan.Id, plan, requester, policy, _clock.GetUtcNow() + plan.Expiry);
        var pending = new Pending(request, publish);
        Task<ConfirmationResult>? rejoined = null;
        lock (_gate)
        {
            // The same requester raising a plan again (a flow recovering a lost ask) re-joins it: the pending answer, or the one given.
            if (_known.Contains(plan.Id))
            {
                if (_pending.TryGetValue(plan.Id, out var asked) && asked.Request.Requester == requester) { rejoined = asked.Done.Task; }
                else if (_audit.FindLast(entry => entry.PlanId == plan.Id && entry.Requester == requester) is { } answered)
                {
                    rejoined = Task.FromResult(new ConfirmationResult(plan.Id, answered.Outcome, answered.Reason, answered.Skipped));
                }
                else { throw new InvalidOperationException($"Confirmation plan '{plan.Id}' was already asked."); }
            }
            else
            {
                _known.Add(plan.Id);
                _pending[plan.Id] = pending;
                _requested.Add(request);
            }
        }
        if (rejoined is not null) { return await rejoined.WaitAsync(ct).ConfigureAwait(false); }
        pending.Expiry = _clock.CreateTimer(_ => _ = End(plan.Id, null, ConfirmationOutcome.Expired, ConfirmationDenyReason.Expired),
            null, plan.Expiry, Timeout.InfiniteTimeSpan);
        if (publish is not null) { await publish(request).ConfigureAwait(false); }
        if (Volatile.Read(ref _approveAll)) { await Resolve(plan.Id, approve: true, Current).ConfigureAwait(false); }
        return await pending.Done.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task End(string planId, ConfirmationPrincipal? approver, ConfirmationOutcome outcome, ConfirmationDenyReason reason)
    {
        Pending? pending;
        ConfirmationResolved resolved;
        lock (_gate)
        {
            if (!_pending.Remove(planId, out pending)) { return; }
            var plan = pending.Request.Plan;
            resolved = new ConfirmationResolved(planId, plan.Action, plan.TargetLabel, pending.Request.Requester, approver, pending.Request.Policy,
                outcome, reason, Skipped: false, _clock.GetUtcNow());
            _audit.Add(resolved);
        }
        pending.Expiry?.Dispose();
        if (pending.Publish is not null) { await pending.Publish(resolved).ConfigureAwait(false); }
        pending.Done.TrySetResult(new ConfirmationResult(planId, outcome, reason, Skipped: false));
    }

    private sealed class Pending(ConfirmationRequested request, Func<Signal, Task>? publish)
    {
        public ConfirmationRequested Request { get; } = request;

        public Func<Signal, Task>? Publish { get; } = publish;

        public TaskCompletionSource<ConfirmationResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ITimer? Expiry { get; set; }
    }

    private sealed class Requester(TestConfirmations owner, ConfirmationPrincipal requester, Func<Signal, Task>? publish) : IConfirmations
    {
        public ConfirmationPolicy Policy => owner.Policy;

        public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default) => owner.Confirm(requester, publish, plan, ct);

        // The caller of this instance is the requester itself: an app or agent relaying an approval is denied SelfApproval.
        public Task Resolve(string planId, bool approve, CancellationToken ct = default) => owner.Resolve(planId, approve, requester);
    }
}
