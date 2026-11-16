using DigitalBrain.Access;
using DigitalBrain.Confirmation;
using DigitalBrain.UI;

namespace DigitalBrain.Testing;

/// <summary>
/// The confirmation gate in <see cref="TestOS"/> (07 D-03, D-09, D-16, D-19): drives and observes the real
/// <see cref="ConfirmationsNeuron"/> hosted in each brain with stamped <see cref="Caller"/> authority.
/// </summary>
public sealed class TestConfirmations : IConfirmationRequests, IConfirmationInbox
{
    private readonly Lock _gate = new();
    private readonly TestOS _os;
    private readonly List<ConfirmationRequested> _osRequested = [];
    private readonly List<ConfirmationRequested> _osWaiting = [];
    private readonly Dictionary<string, BrainId> _osPlanBrains = new(StringComparer.Ordinal);
    private readonly Dictionary<BrainId, long> _syncedSeq = [];
    private readonly List<ConfirmationResolved> _audit = [];
    private readonly ApprovalWaiters _waiters = new();
    private bool _approveAll;

    internal TestConfirmations(TestOS os)
    {
        ArgumentNullException.ThrowIfNull(os);
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
                SyncFromJournals();
                return [.. _osRequested];
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
                SyncFromJournals();
                return [.. _osWaiting];
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
                SyncFromJournals();
                return [.. _audit];
            }
        }
    }

    internal IConfirmations ForNeuron(NeuronId owner) => new NeuronBackedConfirmations(this, _os, owner);

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
    }

    // IConfirmationRequests
    public Task<ConfirmationResult?> Request(ConfirmationPlan plan)
    {
        var brains = _os.AllBrains;
        var brain = brains.Count > 0 ? brains[0] : new BrainId("default");
        using var _ = _os.Edge.Enter(CallerFor(Current, brain));
        return _os.Get<IConfirmationRequests>(IConfirmationRequests.Of(brain)).Request(plan);
    }

    public async Task<ConfirmationResult?> Outcome(string planId)
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

    public async Task Withdraw(string planId)
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
    }

    // IConfirmationInbox
    public async Task<ConfirmationInbox> Read()
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

    Task IConfirmationInbox.Resolve(string planId, bool approve) => Resolve(planId, approve, Current);

    private void SyncFromJournals()
    {
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
        using var _ = _os.Edge.Enter(CallerFor(approver, brain));
        await _os.Get<IConfirmationInbox>(IConfirmationRequests.Of(brain)).Resolve(planId, approve).ConfigureAwait(false);
    }

    private Caller CallerFor(ConfirmationPrincipal principal, BrainId brain) => principal.Kind switch
    {
        PrincipalKind.Person => _os.Edge.Person(principal.Id, principal.Id, brain),
        PrincipalKind.App => _os.Edge.App(principal.Id, new Actor.Person("owner", "owner"), brain),
        _ => _os.Edge.Agent(new Actor.Person(principal.Id, principal.Id), brain),
    };

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
