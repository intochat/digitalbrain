using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

internal interface IBehaviorTaskOperationAccess
{
    ValueTask<TaskOperationSnapshot> PrepareAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        int sequence,
        TaskOperationEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken);

    ValueTask<ReadTaskOperationResult> ReadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        int sequence,
        CancellationToken cancellationToken);

    ValueTask<TaskOperationSnapshot> TransitionAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        int sequence,
        TaskOperationPhase expectedPhase,
        TaskOperationPhase phase,
        ProtectedPayloadReference? responsePayload,
        string? redactedSummary,
        CancellationToken cancellationToken);
}

internal sealed class GrainBehaviorTaskOperationAccess(IGrainFactory grains) : IBehaviorTaskOperationAccess
{
    private static readonly TimeSpan OperationWaitBound = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JournalPollInterval = TimeSpan.FromMilliseconds(50);

    public ValueTask<TaskOperationSnapshot> PrepareAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        int sequence,
        TaskOperationEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
        => StageAndAwaitAsync<TaskOperationSnapshot>(
            owner,
            task,
            attempt,
            requireActivation: true,
            (broker, boundTask, token) => broker.StagePrepare(
                boundTask,
                new PrepareTaskOperation(attempt, sequence, edge, requestPayload),
                token),
            cancellationToken);

    public async ValueTask<ReadTaskOperationResult> ReadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        int sequence,
        CancellationToken cancellationToken)
    {
        return await StageAndAwaitAsync<ReadTaskOperationResult>(
            owner,
            task,
            attempt,
            requireActivation: false,
            (broker, boundTask, token) => broker.StageRead(
                boundTask,
                new ReadTaskOperation(attempt, sequence),
                token),
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<TaskOperationSnapshot> TransitionAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        int sequence,
        TaskOperationPhase expectedPhase,
        TaskOperationPhase phase,
        ProtectedPayloadReference? responsePayload,
        string? redactedSummary,
        CancellationToken cancellationToken)
        => StageAndAwaitAsync<TaskOperationSnapshot>(
            owner,
            task,
            attempt,
            requireActivation: true,
            (broker, boundTask, token) => broker.StageTransition(
                boundTask,
                new TransitionTaskOperation(
                    attempt,
                    sequence,
                    expectedPhase,
                    phase,
                    responsePayload,
                    redactedSummary),
                token),
            cancellationToken);

    private async ValueTask<TResponse> StageAndAwaitAsync<TResponse>(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        bool requireActivation,
        Func<IBehaviorWorkerBroker, NeuronId, CancellationToken, Task<WorkerOperationReceipt>> stage,
        CancellationToken cancellationToken)
        where TResponse : Synapse
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(stage);
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = await ReadAndValidateTaskAsync(owner, task, attempt, requireActivation, cancellationToken)
            .ConfigureAwait(false);
        var worker = snapshot.Worker;
        var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(owner).ToGrainId());
        var broker = grains.GetGrain<IBehaviorWorkerBroker>(worker.ToGrainId());

        var cursor = await session
            .ReadNeuronJournal(worker, JournalKind.Incoming, afterSequence: 0)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(OperationWaitBound);

        try
        {
            var receipt = await stage(broker, task, bound.Token).ConfigureAwait(false);
            if (receipt.Worker != worker || receipt.Task != task)
            {
                throw new InvalidOperationException("worker-mismatch");
            }

            return await PollJournalAsync<TResponse>(
                    session,
                    worker,
                    receipt.Correlation,
                    task,
                    cursor.ResumeSequence,
                    bound.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("operation-timeout");
        }
    }

    private async Task<TaskSnapshot> ReadAndValidateTaskAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        bool requireActivation,
        CancellationToken cancellationToken)
    {
        if (owner == default)
        {
            throw new ArgumentException("missing-owner", paramName: null);
        }

        if (task == default || string.IsNullOrWhiteSpace(task.Type) || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new ArgumentException("missing-task-identity", paramName: null);
        }

        if (task.Owner != owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        if (!string.Equals(
                task.Type,
                NeuronId.GrainTypeNameOf(typeof(ITask)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("invalid-task-identity");
        }

        if (attempt == default || attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("invalid-attempt", paramName: null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var authority = grains.GetGrain<IBehaviorTaskAuthority>(
            BehaviorTaskAuthority.ForOwner(owner).ToGrainId());
        return await authority
            .ReadValidatedTask(task, attempt, requireActivation, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<TResponse> PollJournalAsync<TResponse>(
        ISessionNeuron session,
        NeuronId worker,
        CorrelationId correlation,
        NeuronId expectedTaskCaller,
        long afterSequence,
        CancellationToken cancellationToken)
        where TResponse : Synapse
    {
        var cursor = afterSequence;
        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await session
                .ReadNeuronJournal(worker, JournalKind.Incoming, cursor)
                .ConfigureAwait(false);

            if (page.ResetSnapshot is not null)
            {
                cursor = 0;
            }

            if (TryMatch<TResponse>(page, correlation, expectedTaskCaller, out var response))
            {
                return response;
            }

            if (page.ResumeSequence > cursor)
            {
                cursor = page.ResumeSequence;
            }

            await Task.Delay(JournalPollInterval, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("operation-timeout");
    }

    private static bool TryMatch<TResponse>(
        JournalRead page,
        CorrelationId correlation,
        NeuronId expectedTaskCaller,
        out TResponse response)
        where TResponse : Synapse
    {
        foreach (var delivery in page.Delta)
        {
            if (delivery.CorrelationId == correlation
                && delivery.Caller == expectedTaskCaller
                && delivery.Synapse is TResponse matched)
            {
                response = matched;
                return true;
            }
        }

        response = null!;
        return false;
    }
}
