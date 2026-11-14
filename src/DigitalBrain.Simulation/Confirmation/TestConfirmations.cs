using DigitalBrain.Access;
using DigitalBrain.Confirmation;
using DigitalBrain.UI;
using DigitalBrain.UI.Confirmation;

namespace DigitalBrain.Testing;

/// <summary>
/// The confirmation gate in tests (07 D-03, D-09, D-16, D-19): when attached to a <see cref="TestOS"/>, drives and observes the
/// real <see cref="ConfirmationsNeuron"/> hosted in each brain with stamped <see cref="Caller"/> authority; standalone (unit harness
/// without <see cref="TestOS"/>), uses pure <see cref="ConfirmationState"/> directly.
/// </summary>
public sealed class TestConfirmations : IConfirmationRequests, IConfirmationInbox
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private readonly TestOS? _os;
    private ConfirmationState _state = new();
    private readonly Dictionary<string, TaskCompletionSource<ConfirmationResult>> _done = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Func<Signal, Task>? Publish, ITimer? Expiry)> _pending = new(StringComparer.Ordinal);
    private readonly List<ConfirmationRequested> _osRequested = [];
    private readonly List<ConfirmationRequested> _osWaiting = [];
    private readonly Dictionary<string, BrainId> _osPlanBrains = new(StringComparer.Ordinal);
    private readonly Dictionary<BrainId, long> _syncedSeq = [];
    private readonly List<ConfirmationResolved> _audit = [];
    private readonly ApprovalWaiters _waiters = new();
    private bool _approveAll;

    /// <summary>Confirmations on <paramref name="clock"/>.</summary>
    public TestConfirmations(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    internal TestConfirmations(TimeProvider clock, TestOS os)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(os);
        _clock = clock;
        _os = os;

        _ = os.Listeners.Listen<ConfirmationRequested>("testos.confirmations.requested", async (publisher, signal, _) =>
        {
            if (publisher.Type != ConfirmationsNeuron.NeuronType) { return; }
            lock (_gate) { SyncFromJournals(); }
            if (Volatile.Read(ref _approveAll))
            {
                await ResolveInBrain(publisher.Brain, signal.PlanId, approve: true, Current).ConfigureAwait(false);
            }
        });

        _ = os.Listeners.Listen<ConfirmationResolved>("testos.confirmations.resolved", (publisher, signal, _) =>
        {
            if (publisher.Type != ConfirmationsNeuron.NeuronType) { return Task.CompletedTask; }
            lock (_gate) { SyncFromJournals(); }
            _waiters.Complete(signal.PlanId, new ConfirmationResult(signal.PlanId, signal.Outcome, signal.Reason, signal.Skipped));
            return Task.CompletedTask;
        });
    }

    /// <summary>The brain policy. Default <see cref="ConfirmationPolicy.DestructiveOnly"/>.</summary>
    public ConfirmationPolicy Policy { get; set; } = ConfirmationPolicy.DestructiveOnly;

    /// <summary>The principal viewing the screen; <see cref="Approve"/>, <see cref="Deny"/> and <see cref="Click"/> answer as it. Default a person.</summary>
    public ConfirmationPrincipal Current { get; set; } = ConfirmationPrincipal.Person("person");

    /// <summary>Every plan put to a person, oldest first.</summary>
    public IReadOnlyList<ConfirmationRequested> Requested
    {
        get
        {
            lock (_gate)
            {
                if (_os is not null)
                {
                    SyncFromJournals();
                    return [.. _osRequested];
                }
                return [.. _state.Entries.Where(e => e.Result?.Skipped != true).Select(e => e.Request)];
            }
        }
    }

    /// <summary>The plans still waiting for an answer.</summary>
    public IReadOnlyList<ConfirmationRequested> Waiting
    {
        get
        {
            lock (_gate)
            {
                if (_os is not null)
                {
                    SyncFromJournals();
                    return [.. _osWaiting];
                }
                return [.. _state.Entries.Where(e => e.Result is null).Select(e => e.Request)];
            }
        }
    }

    /// <summary>Every resolution, skipped ones included, oldest first: the audit record.</summary>
    public IReadOnlyList<ConfirmationResolved> Audit
    {
        get
        {
            lock (_gate)
            {
                if (_os is not null) { SyncFromJournals(); }
                return [.. _audit];
            }
        }
    }

    /// <summary>The gate as <paramref name="requester"/> sees it; signals also go to <paramref name="publish"/> when given.</summary>
    public IConfirmations For(ConfirmationPrincipal requester, Func<Signal, Task>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(requester);
        return new Requester(this, requester, publish);
    }

    internal IConfirmations ForNeuron(NeuronId owner)
    {
        if (_os is null) { throw new InvalidOperationException("Not attached to a TestOS."); }
        return new NeuronBackedConfirmations(this, _os, owner);
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

        if (_os is not null)
        {
            BrainId? known = null;
            lock (_gate)
            {
                SyncFromJournals();
                if (_osPlanBrains.TryGetValue(planId, out var brain)) { known = brain; }
            }
            if (known is { } targetBrain)
            {
                await ResolveInBrain(targetBrain, planId, approve, approver).ConfigureAwait(false);
                return;
            }
            foreach (var brain in _os.AllBrains)
            {
                await ResolveInBrain(brain, planId, approve, approver).ConfigureAwait(false);
            }
            return;
        }

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
    public Task<ConfirmationResult?> Request(ConfirmationPlan plan)
    {
        if (_os is not null)
        {
            var brains = _os.AllBrains;
            var brain = brains.Count > 0 ? brains[0] : new BrainId("default");
            using var _ = _os.Edge.Enter(CallerFor(Current, brain));
            return _os.Get<IConfirmationRequests>(IConfirmationRequests.Of(brain)).Request(plan);
        }
        return Request(plan, Current, "edge", null);
    }

    public async Task<ConfirmationResult?> Outcome(string planId)
    {
        if (_os is not null)
        {
            BrainId? known = null;
            lock (_gate)
            {
                SyncFromJournals();
                if (_osPlanBrains.TryGetValue(planId, out var brain)) { known = brain; }
            }
            if (known is { } targetBrain)
            {
                using var _ = _os.Edge.Enter(CallerFor(Current, targetBrain));
                return await _os.Get<IConfirmationRequests>(IConfirmationRequests.Of(targetBrain)).Outcome(planId).ConfigureAwait(false);
            }
            foreach (var brain in _os.AllBrains)
            {
                using var _ = _os.Edge.Enter(CallerFor(Current, brain));
                var found = await _os.Get<IConfirmationRequests>(IConfirmationRequests.Of(brain)).Outcome(planId).ConfigureAwait(false);
                if (found is not null) { return found; }
            }
            return null;
        }
        lock (_gate)
        {
            return _state.Known(planId);
        }
    }

    public async Task Withdraw(string planId)
    {
        if (_os is not null)
        {
            BrainId? known = null;
            lock (_gate)
            {
                SyncFromJournals();
                if (_osPlanBrains.TryGetValue(planId, out var brain)) { known = brain; }
            }
            if (known is { } targetBrain)
            {
                using var _ = _os.Edge.Enter(CallerFor(Current, targetBrain));
                await _os.Get<IConfirmationRequests>(IConfirmationRequests.Of(targetBrain)).Withdraw(planId).ConfigureAwait(false);
                return;
            }
            foreach (var brain in _os.AllBrains)
            {
                using var _ = _os.Edge.Enter(CallerFor(Current, brain));
                await _os.Get<IConfirmationRequests>(IConfirmationRequests.Of(brain)).Withdraw(planId).ConfigureAwait(false);
            }
            return;
        }

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
    public async Task<ConfirmationInbox> Read()
    {
        if (_os is not null)
        {
            var pending = new List<ConfirmationRequested>();
            long revision = 0;
            foreach (var brain in _os.AllBrains)
            {
                using var _ = _os.Edge.Enter(CallerFor(Current, brain));
                var inbox = await _os.Get<IConfirmationInbox>(IConfirmationRequests.Of(brain)).Read().ConfigureAwait(false);
                revision += inbox.Revision;
                pending.AddRange(inbox.Pending);
            }
            return new ConfirmationInbox(revision, [.. pending]);
        }

        lock (_gate)
        {
            return new ConfirmationInbox(_state.Revision, [.. _state.Entries.Where(e => e.Result is null).Select(e => e.Request)]);
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

    private void SyncFromJournals()
    {
        if (_os is null) { return; }
        foreach (var brain in _os.AllBrains)
        {
            var last = _syncedSeq.GetValueOrDefault(brain, 0L);
            foreach (var entry in _os.Journal(brain).Signals(IConfirmationRequests.Of(brain)))
            {
                if (entry.Sequence <= last) { continue; }
                last = entry.Sequence;
                switch (entry.Signal)
                {
                    case ConfirmationRequested req:
                        _osPlanBrains[req.PlanId] = brain;
                        _osRequested.Add(req);
                        if (_osWaiting.All(w => w.PlanId != req.PlanId))
                        {
                            _osWaiting.Add(req);
                        }
                        break;
                    case ConfirmationResolved res:
                        _osPlanBrains[res.PlanId] = brain;
                        _osWaiting.RemoveAll(w => w.PlanId == res.PlanId);
                        _audit.Add(res);
                        break;
                }
            }
            _syncedSeq[brain] = last;
        }
    }

    private async Task ResolveInBrain(BrainId brain, string planId, bool approve, ConfirmationPrincipal approver)
    {
        using var _ = _os!.Edge.Enter(CallerFor(approver, brain));
        await _os.Get<IConfirmationInbox>(IConfirmationRequests.Of(brain)).Resolve(planId, approve).ConfigureAwait(false);
    }

    private Caller CallerFor(ConfirmationPrincipal principal, BrainId brain) => principal.Kind switch
    {
        PrincipalKind.Person => _os!.Edge.Person(principal.Id, principal.Id, brain),
        PrincipalKind.App => _os!.Edge.App(principal.Id, new Actor.Person("owner", "owner"), brain),
        _ => _os!.Edge.Agent(new Actor.Person(principal.Id, principal.Id), brain),
    };

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

    private sealed class NeuronBackedConfirmations(TestConfirmations owner, TestOS os, NeuronId callerNeuron) : IConfirmations
    {
        public ConfirmationPolicy Policy =>
            (os.Services.GetService(typeof(ConfirmationPolicies)) as ConfirmationPolicies)?.For(callerNeuron.Brain.Value!)
            ?? owner.Policy;

        public async Task<ConfirmationResult> Confirm(ConfirmationPlan plan, CancellationToken ct = default)
        {
            var target = IConfirmationRequests.Of(callerNeuron.Brain);
            var req = os.Proxy<IConfirmationRequests>(callerNeuron, target);
            var inbox = os.Proxy<IConfirmationInbox>(callerNeuron, target);
            var awaited = new AwaitedConfirmations(req, inbox, Policy, owner._waiters);
            using var scope = EnsureAppCallerIfUnstamped();
            return await awaited.Confirm(plan, ct).ConfigureAwait(false);
        }

        public async Task Resolve(string planId, bool approve, CancellationToken ct = default)
        {
            var target = IConfirmationRequests.Of(callerNeuron.Brain);
            var req = os.Proxy<IConfirmationRequests>(callerNeuron, target);
            var inbox = os.Proxy<IConfirmationInbox>(callerNeuron, target);
            var awaited = new AwaitedConfirmations(req, inbox, Policy, owner._waiters);
            using var scope = EnsureAppCallerIfUnstamped();
            await awaited.Resolve(planId, approve, ct).ConfigureAwait(false);
        }

        private IDisposable? EnsureAppCallerIfUnstamped()
        {
            var current = TestCallerAmbient.Instance.Current;
            if (current is not null && current.Actor is not Actor.Kernel)
            {
                return null;
            }
            return os.Edge.Enter(os.Edge.App(callerNeuron.ToString(), new Actor.Person("owner", "owner"), callerNeuron.Brain));
        }
    }
}
