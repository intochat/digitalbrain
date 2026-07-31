using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TasksHarnessModule : IModule;

[GrainType(GrainTypeName)]
internal sealed class ScriptedWorker :
    Neuron,
    IWorker,
    IHandle<DispatchWorkerAccept>,
    IHandle<DispatchWorkerContinue>,
    IHandle<DispatchWorkerCancel>,
    IHandle<PrepareOperationProbe>,
    IHandle<TransitionOperationProbe>,
    IHandle<TaskOperationSnapshot>,
    IHandle<UserActionParkReady>,
    IHandle<CompleteParkedUserAction>,
    IHandle<DenyParkedUserAction>,
    IHandle<ProbeUserActionCompletionDisposition>
{
    internal const string GrainTypeName = "worker";

    private Goal? lastGoal;
    private IssuedUserActionPark? issuedPark;

    public Task HandleAsync(DispatchWorkerAccept command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Accept(command.Request);
    }

    public async Task HandleAsync(DispatchWorkerContinue command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Narrow harness mode for the production outbox attempt-timeout fact: block in a
        // cancellation-aware validation/restage gate until the handler token is canceled by
        // Neuron.Outbox's DeliveryAttemptTimeout — never by a test-owned CTS.
        if (ContinueCancellationGate.IsArmed(Id))
        {
            var entries = ContinueCancellationGate.Enter(Id);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                // Delay completed without cancel — must not restage under the armed gate.
                ContinueCancellationProbe.Record(
                    Id,
                    new ContinueCancellationObservation(
                        cancellationToken.CanBeCanceled,
                        TurnCancellationToken.CanBeCanceled,
                        cancellationToken.Equals(default),
                        TurnCancellationToken.Equals(default),
                        RestagedContinue: false,
                        HandlerTokenWasAlreadyCanceled: false,
                        HandlerTokenCanceledDuringValidationGate: false,
                        ValidationGateEntries: entries,
                        DeliveryAcknowledgedByRestage: false));
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ContinueCancellationProbe.Record(
                    Id,
                    new ContinueCancellationObservation(
                        cancellationToken.CanBeCanceled,
                        TurnCancellationToken.CanBeCanceled,
                        cancellationToken.Equals(default),
                        TurnCancellationToken.Equals(default),
                        RestagedContinue: false,
                        HandlerTokenWasAlreadyCanceled: false,
                        HandlerTokenCanceledDuringValidationGate: true,
                        ValidationGateEntries: entries,
                        DeliveryAcknowledgedByRestage: false));
                throw;
            }
        }

        // Observe the delivery/lifecycle token the durable outbox path actually bound for this turn.
        // Already-canceled tokens must abort before Continue restage (validation gate).
        var alreadyCanceled = cancellationToken.IsCancellationRequested
            || TurnCancellationToken.IsCancellationRequested;
        if (alreadyCanceled)
        {
            ContinueCancellationProbe.Record(
                Id,
                new ContinueCancellationObservation(
                    cancellationToken.CanBeCanceled,
                    TurnCancellationToken.CanBeCanceled,
                    cancellationToken.Equals(default),
                    TurnCancellationToken.Equals(default),
                    RestagedContinue: false,
                    HandlerTokenWasAlreadyCanceled: true,
                    HandlerTokenCanceledDuringValidationGate: false,
                    ValidationGateEntries: 0,
                    DeliveryAcknowledgedByRestage: false));
            cancellationToken.ThrowIfCancellationRequested();
            TurnCancellationToken.ThrowIfCancellationRequested();
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Continue(command.Cursor);
        ContinueCancellationProbe.Record(
            Id,
            new ContinueCancellationObservation(
                cancellationToken.CanBeCanceled,
                TurnCancellationToken.CanBeCanceled,
                cancellationToken.Equals(default),
                TurnCancellationToken.Equals(default),
                RestagedContinue: true,
                HandlerTokenWasAlreadyCanceled: false,
                HandlerTokenCanceledDuringValidationGate: false,
                ValidationGateEntries: 0,
                DeliveryAcknowledgedByRestage: true));
    }

    public Task HandleAsync(DispatchWorkerCancel command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        return Cancel(command.Cursor);
    }

    public async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lastGoal = request.Goal;

        await SendAsync(
            request.Task,
            new AttemptAccepted(request.Task, request.Worker, request.Attempt, request.Revision));

        switch (request.Goal)
        {
            case RetryableFailureGoal when request.Revision == 0:
                await SendAsync(
                    request.Task,
                    new AttemptFailed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        TaskFixtures.Retryable,
                        Retryable: true));
                return;

            case SuccessGoal:
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision,
                        TaskFixtures.Done,
                        Evidence:
                        [
                            new FactReference(request.Worker, SynapseId.New()),
                        ]));
                return;

            case ProgressGoal:
                await SendAsync(
                    request.Task,
                    new AttemptProgressed(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision));
                return;

            case StaleProbeGoal:
                await SendAsync(
                    request.Task,
                    new AttemptSucceeded(
                        request.Task,
                        request.Worker,
                        request.Attempt,
                        request.Revision + 1,
                        TaskFixtures.StaleSuccess,
                        Evidence: []));
                return;

            case UserActionParkGoal park:
            {
                var custody = ServiceProvider.GetRequiredService<IUserActionCustody>();
                var module = new NeuronId("tasks.user-action-module", request.Task.Owner, park.ModuleName);
                var now = TimeProvider.GetUtcNow();
                var lifetime = park.ExpiresAt - now;
                if (lifetime <= TimeSpan.Zero)
                {
                    lifetime = TimeSpan.FromMinutes(30);
                }

                var actionEpoch = Guid.NewGuid();
                // Completer is this worker: only the parked attempt's worker may resume via harness controls.
                var issued = await custody.IssueAsync(
                    request.Task.Owner,
                    request.Task,
                    request.Attempt,
                    module,
                    park.ModuleId,
                    park.DisplayText,
                    ModuleUserActionBoundary.ProtectActionMaterial(
                        signInUrl: new Uri("https://auth.example.test/oauth"),
                        state: "harness-state"),
                    parkRevision: request.Revision,
                    lifetime,
                    completer: request.Worker,
                    actionEpoch,
                    CancellationToken.None);

                // Align expiry to the silo clock (tests pin a future epoch; custody may use System).
                var expiresAt = now + lifetime;
                var requirement = issued.Requirement with
                {
                    ExpiresAt = expiresAt,
                    ActionReference = new ProtectedPayloadReference(
                        issued.Requirement.ActionReference.Id,
                        expiresAt),
                };
                issuedPark = new IssuedUserActionPark(
                    request.Task,
                    request.Attempt,
                    module,
                    park.ModuleId,
                    requirement.ActionReference,
                    requirement.ActionEpoch,
                    request.Revision,
                    expiresAt);
                await SendAsync(request.Task, requirement);
                return;
            }

            case UserActionContinueSuccessGoal:
                return;
        }
    }

    public Task HandleAsync(UserActionParkReady parkReady, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parkReady);
        cancellationToken.ThrowIfCancellationRequested();
        RequireIssuedParkReady(parkReady);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(CompleteParkedUserAction command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Direct Deliver so Task authority is evaluated with this worker as Caller.
            await GrainFactory.GetGrain<INeuron>(command.Task.ToGrainId()).Deliver(
                SynapseDelivery.Create(
                    new CompleteUserAction(
                        CommandId.New(),
                        command.ActionReference,
                        command.ActionEpoch,
                        command.ExpectedParkRevision),
                    Id,
                    sequence: 1,
                    cause: null,
                    TimeProvider),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Fail-closed at Task; do not poison the session outbox with redelivery.
        }
        catch (NeuronAuthorizationException)
        {
        }
    }

    public async Task HandleAsync(DenyParkedUserAction command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await GrainFactory.GetGrain<INeuron>(command.Task.ToGrainId()).Deliver(
                SynapseDelivery.Create(
                    new DenyUserAction(
                        CommandId.New(),
                        command.ActionReference,
                        command.ActionEpoch,
                        command.ExpectedParkRevision),
                    Id,
                    sequence: 1,
                    cause: null,
                    TimeProvider),
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NeuronAuthorizationException)
        {
        }
    }

    public async Task HandleAsync(ProbeUserActionCompletionDisposition command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        UserActionCompletionDispositionProbe.Clear(command.ProbeId);

        try
        {
            await GrainFactory.GetGrain<INeuron>(command.Task.ToGrainId()).Deliver(
                SynapseDelivery.Create(
                    new CompleteUserAction(
                        CommandId.New(),
                        command.ActionReference,
                        command.ActionEpoch,
                        command.ExpectedParkRevision),
                    Id,
                    sequence: 1,
                    cause: null,
                    TimeProvider),
                cancellationToken);
            UserActionCompletionDispositionProbe.Record(command.ProbeId, "accepted");
        }
        catch (NeuronAuthorizationException refusal)
        {
            UserActionCompletionDispositionProbe.Record(
                command.ProbeId,
                $"{nameof(NeuronAuthorizationException)}:{refusal.Message}");
        }
        catch (InvalidOperationException deferred)
        {
            UserActionCompletionDispositionProbe.Record(
                command.ProbeId,
                $"{nameof(InvalidOperationException)}:{deferred.Message}");
        }
    }

    public async Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        // ProgressGoal only proves Continue dispatch staging; user-action parks resume by succeeding.
        if (lastGoal is ProgressGoal)
        {
            return;
        }

        if (lastGoal is UserActionParkGoal or UserActionContinueSuccessGoal or null)
        {
            await SendAsync(
                cursor.Task,
                new AttemptSucceeded(
                    cursor.Task,
                    cursor.Worker,
                    cursor.Attempt,
                    cursor.Revision,
                    TaskFixtures.Done,
                    Evidence:
                    [
                        new FactReference(cursor.Worker, SynapseId.New()),
                    ]));
        }
    }

    public Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        return SendAsync(cursor.Task, new AttemptCancelled(cursor.Task, cursor.Worker, cursor.Attempt, cursor.Revision));
    }

    public async Task HandleAsync(PrepareOperationProbe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();

        await SendAsync(
            probe.Task,
            new PrepareTaskOperation(probe.Attempt, probe.Sequence, probe.Edge, probe.RequestPayload));
    }

    public async Task HandleAsync(TransitionOperationProbe probe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(probe);
        cancellationToken.ThrowIfCancellationRequested();

        await SendAsync(
            probe.Task,
            new TransitionTaskOperation(
                probe.Attempt,
                probe.Sequence,
                probe.ExpectedPhase,
                probe.Phase,
                probe.ResponsePayload,
                RedactedSummary: null));
    }

    public Task HandleAsync(TaskOperationSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private void RequireIssuedParkReady(UserActionParkReady parkReady)
    {
        if (parkReady.Completer != Id)
        {
            throw new NeuronAuthorizationException("harness-worker-park-ready-completer-mismatch");
        }

        if (CurrentDeliveryCaller is not { } caller || caller != parkReady.Task)
        {
            throw new NeuronAuthorizationException("harness-worker-park-ready-untrusted-caller");
        }

        if (issuedPark is null)
        {
            throw new NeuronAuthorizationException("harness-worker-park-ready-unbound");
        }

        if (parkReady.Task != issuedPark.Task
            || parkReady.Attempt != issuedPark.Attempt
            || parkReady.Module != issuedPark.Module
            || parkReady.ActionEpoch != issuedPark.ActionEpoch
            || parkReady.ActionReference.Id != issuedPark.ActionReference.Id
            || parkReady.ParkRevision != issuedPark.ParkRevision
            || !string.Equals(parkReady.ModuleId, issuedPark.ModuleId, StringComparison.Ordinal))
        {
            throw new NeuronAuthorizationException("harness-worker-park-ready-binding-mismatch");
        }
    }

    private sealed record IssuedUserActionPark(
        NeuronId Task,
        AttemptId Attempt,
        NeuronId Module,
        string ModuleId,
        ProtectedPayloadReference ActionReference,
        Guid ActionEpoch,
        long ParkRevision,
        DateTimeOffset ExpiresAt);
}
