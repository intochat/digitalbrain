using System.Collections.ObjectModel;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;

namespace Brain.Core.Activities;

internal sealed record BrainActivityState
{
    public BrainActivityState(
        BrainActivityId activity,
        OperationId operation,
        WorkspaceContext caller,
        IdempotencyKey idempotencyKey,
        CorrelationId correlation,
        BrainActivityId? parentActivity,
        ContractId terminalResultContract,
        Delegation delegation,
        string inputFingerprint)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(delegation);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFingerprint);
        Activity = activity;
        Operation = operation;
        Caller = caller;
        IdempotencyKey = idempotencyKey;
        Correlation = correlation;
        ParentActivity = parentActivity;
        TerminalResultContract = terminalResultContract;
        Delegation = delegation;
        InputFingerprint = inputFingerprint;
        Status = ActivityStatus.Accepted;
    }

    public BrainActivityId Activity { get; init; }

    public OperationId Operation { get; init; }

    public WorkspaceContext Caller { get; init; }

    public IdempotencyKey IdempotencyKey { get; init; }

    public CorrelationId Correlation { get; init; }

    public BrainActivityId? ParentActivity { get; init; }

    public ContractId TerminalResultContract { get; init; }

    public Delegation Delegation { get; init; }

    // A one-way, stable comparison value for idempotency. The original input is never persisted.
    public string InputFingerprint { get; init; }

    public ActivityStatus Status { get; init; }

    public ActivityProgressReference? Progress { get; init; }

    public ActivityResultReference? Result { get; init; }

    public ActivityProblem? Problem { get; init; }
}

internal readonly record struct ActivityIdempotencyIdentity(
    WorkspaceId Workspace,
    PrincipalId Principal,
    IdempotencyKey Key);

internal interface IActivityStore
{
    BrainActivityState Get(BrainActivityId activity);

    BrainActivityState GetOrAdd(
        ActivityIdempotencyIdentity identity,
        Func<BrainActivityState> create,
        out bool created);

    void CreateAccepted(BrainActivityState state);

    void Save(BrainActivityState state);
}

internal sealed class InMemoryActivityStore : IActivityStore
{
    private readonly object _gate = new();
    private readonly Dictionary<BrainActivityId, BrainActivityState> _activities = [];
    private readonly Dictionary<ActivityIdempotencyIdentity, BrainActivityId> _idempotency = [];

    internal IReadOnlyDictionary<BrainActivityId, BrainActivityState> Activities
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyDictionary<BrainActivityId, BrainActivityState>(
                    new Dictionary<BrainActivityId, BrainActivityState>(_activities));
            }
        }
    }

    public BrainActivityState Get(BrainActivityId activity)
    {
        lock (_gate)
        {
            return _activities.TryGetValue(activity, out var state)
                ? state
                : throw new KeyNotFoundException($"Activity '{activity}' was not found.");
        }
    }

    public BrainActivityState GetOrAdd(
        ActivityIdempotencyIdentity identity,
        Func<BrainActivityState> create,
        out bool created)
    {
        ArgumentNullException.ThrowIfNull(create);
        lock (_gate)
        {
            if (_idempotency.TryGetValue(identity, out var existing))
            {
                created = false;
                return _activities[existing];
            }

            var state = create();
            Add(state);
            created = true;
            return state;
        }
    }

    public void CreateAccepted(BrainActivityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            Add(state);
        }
    }

    public void Save(BrainActivityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            if (!_activities.ContainsKey(state.Activity))
            {
                throw new KeyNotFoundException($"Activity '{state.Activity}' was not found.");
            }

            _activities[state.Activity] = state;
        }
    }

    private void Add(BrainActivityState state)
    {
        if (_activities.ContainsKey(state.Activity))
        {
            throw new InvalidOperationException($"Activity '{state.Activity}' already exists.");
        }

        var identity = new ActivityIdempotencyIdentity(
            state.Caller.Workspace,
            state.Caller.Principal,
            state.IdempotencyKey);
        if (_idempotency.ContainsKey(identity))
        {
            throw new IdempotencyConflictException(
                "An activity already exists for this workspace, principal, and idempotency key.");
        }

        _activities.Add(state.Activity, state);
        _idempotency.Add(identity, state.Activity);
    }
}
