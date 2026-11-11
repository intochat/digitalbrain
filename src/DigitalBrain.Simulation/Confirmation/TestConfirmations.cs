using DigitalBrain.Confirmation;
using DigitalBrain.UI;
using DigitalBrain.UI.Confirmation;

namespace DigitalBrain.Testing;

/// <summary>
/// The confirmation gate in tests (07 D-03, D-09, D-16, D-19): uses pure <see cref="ConfirmationState"/> to enforce
/// all 4 core invariants (20 pending limit, 128 ID length cap, 5 minute default expiry cap, idempotent re-raise by same caller/neuron).
/// </summary>
public sealed class TestConfirmations : IConfirmationRequests, IConfirmationInbox
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private ConfirmationState _state = new();
    private readonly Dictionary<string, TaskCompletionSource<ConfirmationResult>> _done = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Func<Signal, Task>? Publish, ITimer? Expiry)> _pending = new(StringComparer.Ordinal);
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
        get { lock (_gate) { return [.. _state.Entries.Where(e => e.Result?.Skipped != true).Select(e => e.Request)]; } }
    }

    /// <summary>The plans still waiting for an answer.</summary>
    public IReadOnlyList<ConfirmationRequested> Waiting
    {
        get { lock (_gate) { return [.. _state.Entries.Where(e => e.Result is null).Select(e => e.Request)]; } }
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

    /// <summary>Approves or denies a rendered confirmation as <see cref="Current"/>, as the OS inbox surface does from the node's resolution.</summary>
    /// <exception cref="InvalidOperationException">The card has no resolution: its viewer cannot resolve it.</exception>
    public Task Click(ApprovalCardNode card, bool approve = true)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Resolve is not { } resolve) { throw new InvalidOperationException($"The approval '{card.Key}' cannot be resolved by its viewer."); }
        return Resolve(resolve.PlanId, approve, Current);
    }

    /// <summary>Answers <paramref name="planId"/> as <paramref name="approver"/>. The first answer wins; unknown or answered ids are ignored.</summary>
    public async Task Resolve(string planId, bool approve, ConfirmationPrincipal approver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentNullException.ThrowIfNull(approver);
        var now = _clock.GetUtcNow();
        ConfirmationTransition? transition;
        Func<Signal, Task>? publish = null;
        lock (_gate)
        {
            transition = _state.Answer(planId, approve, approver.IsHuman, approver, now);
            if (transition is null) { return; }
            _state = transition.Value.Next;
            if (_pending.Remove(planId, out var p))
            {
                p.Expiry?.Dispose();
                publish = p.Publish;
            }
            if (transition.Value.Signal is ConfirmationResolved res)
            {
                _audit.Add(res);
            }
        }
        if (publish is not null) { await publish(transition.Value.Signal).ConfigureAwait(false); }
        if (_done.TryGetValue(planId, out var tcs))
        {
            tcs.TrySetResult(transition.Value.Result!);
        }
    }

    // IConfirmationRequests
    public Task<ConfirmationResult?> Request(ConfirmationPlan plan) => Request(plan, Current, "edge", null);

    public Task<ConfirmationResult?> Outcome(string planId)
    {
        lock (_gate)
        {
            return Task.FromResult(_state.Known(planId));
        }
    }

    public async Task Withdraw(string planId)
    {
        ConfirmationTransition? transition;
        Func<Signal, Task>? publish = null;
        lock (_gate)
        {
            transition = _state.Withdraw(planId, _clock.GetUtcNow());
            if (transition is null) { return; }
            _state = transition.Value.Next;
            if (_pending.Remove(planId, out var p))
            {
                p.Expiry?.Dispose();
                publish = p.Publish;
            }
            if (transition.Value.Signal is ConfirmationResolved res)
            {
                _audit.Add(res);
            }
        }
        if (publish is not null) { await publish(transition.Value.Signal).ConfigureAwait(false); }
        if (_done.TryGetValue(planId, out var tcs))
        {
            tcs.TrySetResult(transition.Value.Result!);
        }
    }

    // IConfirmationInbox
    public Task<ConfirmationInbox> Read()
    {
        lock (_gate)
        {
            return Task.FromResult(new ConfirmationInbox(_state.Revision, [.. _state.Entries.Where(e => e.Result is null).Select(e => e.Request)]));
        }
    }

    Task IConfirmationInbox.Resolve(string planId, bool approve) => Resolve(planId, approve, Current);

    public async Task<ConfirmationResult?> Request(ConfirmationPlan plan, ConfirmationPrincipal requester, string raisedBy, Func<Signal, Task>? publish)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var now = _clock.GetUtcNow();
        ConfirmationTransition? transition;
        lock (_gate)
        {
            transition = _state.Request(plan, requester, raisedBy, Policy, now);
            if (transition is null)
            {
                return _state.Known(plan.Id);
            }
            _state = transition.Value.Next;
            if (transition.Value.Result is null)
            {
                var timer = _clock.CreateTimer(_ => _ = Expire(plan.Id), null, plan.Expiry < ConfirmationRules.DefaultExpiry ? plan.Expiry : ConfirmationRules.DefaultExpiry, Timeout.InfiniteTimeSpan);
                _pending[plan.Id] = (publish, timer);
                _done[plan.Id] = new TaskCompletionSource<ConfirmationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else if (transition.Value.Signal is ConfirmationResolved skipped)
            {
                _audit.Add(skipped);
            }
        }

        if (publish is not null)
        {
            await publish(transition.Value.Signal).ConfigureAwait(false);
        }

        if (transition.Value.Result is not null)
        {
            return transition.Value.Result;
        }

        if (Volatile.Read(ref _approveAll))
        {
            await Resolve(plan.Id, approve: true, Current).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<ConfirmationResult> Confirm(ConfirmationPrincipal requester, Func<Signal, Task>? publish, ConfirmationPlan plan, CancellationToken ct)
    {
        var result = await Request(plan, requester, requester.Id, publish).ConfigureAwait(false);
        if (result is not null) { return result; }
        TaskCompletionSource<ConfirmationResult> tcs;
        lock (_gate)
        {
            tcs = _done[plan.Id];
        }
        return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task Expire(string planId)
    {
        ConfirmationTransition? transition;
        Func<Signal, Task>? publish = null;
        lock (_gate)
        {
            transition = _state.Expire(planId, _clock.GetUtcNow());
            if (transition is null) { return; }
            _state = transition.Value.Next;
            if (_pending.Remove(planId, out var p))
            {
                p.Expiry?.Dispose();
                publish = p.Publish;
            }
            if (transition.Value.Signal is ConfirmationResolved res)
            {
                _audit.Add(res);
            }
        }
        if (publish is not null) { await publish(transition.Value.Signal).ConfigureAwait(false); }
        if (_done.TryGetValue(planId, out var tcs))
        {
            tcs.TrySetResult(transition.Value.Result!);
        }
    }

    private sealed class Requester(TestConfirmations owner, ConfirmationPrincipal requester, Func<Signal, Task>? publish) : IConfirmations
    {
        public ConfirmationPolicy Policy => owner.Policy;

        public Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
            => owner.Confirm(requester, publish, plan, ct);

        public Task Resolve(string planId, bool approve, CancellationToken ct = default)
            => owner.Resolve(planId, approve, requester);
    }
}
