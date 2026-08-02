using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Mcp;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Behaviors;

[GrainType(UserActionCompletionBridge.GrainTypeName)]
internal sealed class UserActionCompletionBridgeNeuron :
    Neuron,
    INeuron,
    IHandle<BindUserActionCompletion>,
    IHandle<AuthorizationCompleted>,
    IHandle<AuthorizationDenied>,
    IHandle<UserActionParkReady>
{
    private const string StateName = "behaviors.user-action-completion-bridge";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<UserActionCompletionBridgeData> _states;

    public UserActionCompletionBridgeNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<UserActionCompletionBridgeData>>();
    }

    Task INeuron.Deliver(SynapseDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();

        if (delivery.Synapse is AuthorizationCompleted or AuthorizationDenied)
        {
            var expected = NeuronId.For<IMcpAuthorization>(Id.Owner, McpAuthorizationNeuron.InstanceName);
            if (delivery.Caller != expected)
            {
                throw new NeuronAuthorizationException("user-action-bridge-requires-mcp-caller");
            }
        }

        if (delivery.Synapse is UserActionParkReady parkReady)
        {
            var data = Load();
            if (data is null || delivery.Caller != data.Task || parkReady.Task != data.Task)
            {
                throw new NeuronAuthorizationException("user-action-bridge-park-ready-untrusted");
            }
        }

        return base.Deliver(delivery, cancellationToken);
    }

    public Task HandleAsync(BindUserActionCompletion bind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bind);
        cancellationToken.ThrowIfCancellationRequested();
        RequireBridgeIdentity(bind.ActionEpoch);
        RequireOwner(bind.Task);
        RequireOwner(bind.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(bind.ModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bind.ServerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(bind.AuthorizationState);

        if (bind.ActionReference.Id == Guid.Empty || bind.ActionEpoch == Guid.Empty)
        {
            throw new InvalidOperationException("invalid-user-action-bridge-bind");
        }

        if (bind.AuthorizationCommandId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("invalid-user-action-bridge-bind");
        }

        return BindCoreAsync(bind, cancellationToken);
    }

    public async Task HandleAsync(AuthorizationCompleted completed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completed);
        cancellationToken.ThrowIfCancellationRequested();

        var data = RequireBound();

        if (!MatchesAuthorization(data, completed.CommandId, completed.ServerKey, completed.State))
        {
            throw new NeuronAuthorizationException("user-action-bridge-authorization-mismatch");
        }

        if (data.Outcome is UserActionCompletionBridgeOutcome.Denied)
        {
            throw new InvalidOperationException("user-action-bridge-already-denied");
        }

        RequireNotExpired(data);

        if (data.Outcome is not UserActionCompletionBridgeOutcome.Completed)
        {
            data = data with { Outcome = UserActionCompletionBridgeOutcome.Completed };
            StageForTurn(data);
        }

        data = await ObserveParkIfWaitingAsync(data, cancellationToken);
        await TryDispatchHeldCompletionAsync(data);
    }

    public async Task HandleAsync(AuthorizationDenied denied, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(denied);
        cancellationToken.ThrowIfCancellationRequested();

        var data = RequireBound();

        if (!MatchesAuthorization(data, denied.CommandId, denied.ServerKey, denied.State))
        {
            throw new NeuronAuthorizationException("user-action-bridge-authorization-mismatch");
        }

        if (data.Outcome is UserActionCompletionBridgeOutcome.Completed)
        {
            throw new InvalidOperationException("user-action-bridge-already-completed");
        }

        RequireNotExpired(data);

        if (data.Outcome is not UserActionCompletionBridgeOutcome.Denied)
        {
            data = data with { Outcome = UserActionCompletionBridgeOutcome.Denied };
            StageForTurn(data);
        }

        data = await ObserveParkIfWaitingAsync(data, cancellationToken);
        await TryDispatchHeldCompletionAsync(data);
    }

    public Task HandleAsync(UserActionParkReady parkReady, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parkReady);
        cancellationToken.ThrowIfCancellationRequested();

        var data = RequireBound();

        RequireExactParkBinding(data, parkReady);

        if (!data.ParkReady)
        {
            data = data with { ParkReady = true };
            StageForTurn(data);
        }

        return TryDispatchHeldCompletionAsync(data);
    }

    private async Task BindCoreAsync(BindUserActionCompletion bind, CancellationToken cancellationToken)
    {
        await RequireAuthorizedProductionBinderAsync(bind, cancellationToken);

        var data = Load();
        if (data is not null)
        {
            if (data.Task == bind.Task
                && data.Attempt == bind.Attempt
                && data.Module == bind.Module
                && data.ActionEpoch == bind.ActionEpoch
                && data.ActionReference == bind.ActionReference
                && data.ParkRevision == bind.ParkRevision
                && data.AuthorizationCommandId == bind.AuthorizationCommandId
                && string.Equals(data.ServerKey, bind.ServerKey, StringComparison.Ordinal)
                && string.Equals(data.AuthorizationState, bind.AuthorizationState, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException("user-action-bridge-already-bound");
        }

        StageForTurn(new UserActionCompletionBridgeData(
            bind.Task,
            bind.Attempt,
            bind.Module,
            bind.ModuleId.Trim(),
            bind.ActionReference,
            bind.ActionEpoch,
            bind.ParkRevision,
            bind.ExpiresAt,
            bind.AuthorizationCommandId,
            bind.ServerKey.Trim(),
            bind.AuthorizationState.Trim(),
            UserActionCompletionBridgeOutcome.Open,
            ParkReady: false));
    }

    private async Task<UserActionCompletionBridgeData> ObserveParkIfWaitingAsync(
        UserActionCompletionBridgeData data,
        CancellationToken cancellationToken)
    {
        if (data.ParkReady)
        {
            return data;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await GrainFactory.GetGrain<ITask>(data.Task.ToGrainId()).Read();
        if (snapshot.State != TaskState.Waiting
            || snapshot.ActiveAttempt != data.Attempt
            || snapshot.Blocker is not UserActionPending pending
            || pending.Completer != Id
            || pending.ActionEpoch != data.ActionEpoch
            || pending.ActionReference.Id != data.ActionReference.Id
            || pending.ParkRevision != data.ParkRevision
            || !string.Equals(pending.ModuleId, data.ModuleId, StringComparison.Ordinal))
        {
            return data;
        }

        data = data with { ParkReady = true };
        StageForTurn(data);
        return data;
    }

    private Task TryDispatchHeldCompletionAsync(UserActionCompletionBridgeData data)
    {
        if (!data.ParkReady || data.Outcome is UserActionCompletionBridgeOutcome.Open)
        {
            return Task.CompletedTask;
        }

        RequireNotExpired(data);

        // Outcome is already staged for this turn (or durable from a prior hold). Outgoing
        // completion is staged into the same turn commit — no intermediate WriteStateAsync.
        return data.Outcome switch
        {
            UserActionCompletionBridgeOutcome.Completed => SendAsync(
                data.Task,
                new CompleteUserAction(
                    CommandId.New(),
                    data.ActionReference,
                    data.ActionEpoch,
                    data.ParkRevision)),
            UserActionCompletionBridgeOutcome.Denied => SendAsync(
                data.Task,
                new DenyUserAction(
                    CommandId.New(),
                    data.ActionReference,
                    data.ActionEpoch,
                    data.ParkRevision)),
            _ => Task.CompletedTask,
        };
    }

    private UserActionCompletionBridgeData RequireBound()
        => Load()
            ?? throw new NeuronAuthorizationException("user-action-bridge-unbound");

    private void RequireNotExpired(UserActionCompletionBridgeData data)
    {
        if (data.ExpiresAt <= TimeProvider.GetUtcNow())
        {
            throw new NeuronAuthorizationException("user-action-bridge-expired");
        }
    }

    private void RequireExactParkBinding(UserActionCompletionBridgeData data, UserActionParkReady parkReady)
    {
        RequireBridgeIdentity(parkReady.ActionEpoch);

        if (parkReady.Completer != Id)
        {
            throw new NeuronAuthorizationException("user-action-bridge-park-ready-completer-mismatch");
        }

        if (parkReady.Task != data.Task
            || parkReady.Attempt != data.Attempt
            || parkReady.Module != data.Module
            || parkReady.ActionEpoch != data.ActionEpoch
            || parkReady.ActionReference.Id != data.ActionReference.Id
            || parkReady.ParkRevision != data.ParkRevision
            || !string.Equals(parkReady.ModuleId, data.ModuleId, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException("user-action-bridge-park-ready-binding-mismatch");
        }
    }

    private void RequireBridgeIdentity(Guid actionEpoch)
    {
        var expected = UserActionCompletionBridge.For(Id.Owner, actionEpoch);
        if (Id != expected)
        {
            throw new NeuronAuthorizationException("user-action-bridge-identity-mismatch");
        }
    }

    private void RequireOwner(NeuronId neuron)
    {
        if (neuron == default || neuron.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException("user-action-bridge-owner-mismatch");
        }
    }

    private async Task RequireAuthorizedProductionBinderAsync(
        BindUserActionCompletion bind,
        CancellationToken cancellationToken)
    {
        if (!GrainCallerContext.TryGetNeuronId(out var binder)
            || binder == default
            || CurrentDeliveryCaller is not { } deliveryCaller
            || binder != deliveryCaller
            || binder.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException("user-action-bridge-untrusted-binder");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = await GrainFactory.GetGrain<ITask>(bind.Task.ToGrainId()).Read();
        if (snapshot.Worker != binder
            || snapshot.ActiveAttempt != bind.Attempt
            || snapshot.State is not (TaskState.Running or TaskState.Pending or TaskState.Waiting))
        {
            throw new NeuronAuthorizationException("user-action-bridge-binder-not-task-worker");
        }
    }

    private static bool MatchesAuthorization(
        UserActionCompletionBridgeData data,
        CommandId commandId,
        string serverKey,
        string state)
        => data.AuthorizationCommandId == commandId
            && string.Equals(data.ServerKey, serverKey, StringComparison.Ordinal)
            && string.Equals(data.AuthorizationState, state, StringComparison.Ordinal);

    private UserActionCompletionBridgeData? Load()
    {
        if (_state.Value is not { Length: > 0 } bytes)
        {
            return null;
        }

        return _states.Deserialize(bytes);
    }

    private void StageForTurn(UserActionCompletionBridgeData data)
    {
        var previous = _state.Value is { Length: > 0 } bytes
            ? bytes.ToArray()
            : [];
        _state.Value = _states.SerializeToArray(data);
        EnlistTurnRollback(() => _state.Value = previous);
    }
}

[GenerateSerializer]
[Alias("behaviors.user-action-completion-bridge-data")]
internal sealed record UserActionCompletionBridgeData(
    [property: Id(0)] NeuronId Task,
    [property: Id(1)] AttemptId Attempt,
    [property: Id(2)] NeuronId Module,
    [property: Id(3)] string ModuleId,
    [property: Id(4)] ProtectedPayloadReference ActionReference,
    [property: Id(5)] Guid ActionEpoch,
    [property: Id(6)] long ParkRevision,
    [property: Id(7)] DateTimeOffset ExpiresAt,
    [property: Id(8)] CommandId AuthorizationCommandId,
    [property: Id(9)] string ServerKey,
    [property: Id(10)] string AuthorizationState,
    [property: Id(11)] UserActionCompletionBridgeOutcome Outcome,
    [property: Id(12)] bool ParkReady = false);

[GenerateSerializer]
[Alias("behaviors.user-action-completion-bridge-outcome")]
internal enum UserActionCompletionBridgeOutcome
{
    Open = 0,
    Completed = 1,
    Denied = 2,
}
