using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Behaviors.Runtime;

internal interface IBehaviorCapabilityDispatchAccess
{
    ValueTask<ProtectedPayloadReference> DispatchAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken);

    ValueTask<string> EmitFactAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        BehaviorId behavior,
        string emitAlias,
        string factJson,
        CancellationToken cancellationToken);
}

internal sealed class GrainBehaviorCapabilityDispatchAccess : IBehaviorCapabilityDispatchAccess
{
    private static readonly TimeSpan OperationWaitBound = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JournalPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly IGrainFactory grains;
    private readonly ActiveCapabilityCatalog catalog;
    private readonly IBehaviorProtectedPayloadAccess payloads;
    private readonly ActiveModuleContractTypeMap typeMap;

    public GrainBehaviorCapabilityDispatchAccess(
        IGrainFactory grains,
        ActiveCapabilityCatalog catalog,
        IBehaviorProtectedPayloadAccess payloads,
        ActiveModuleContractTypeMap typeMap)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(payloads);
        ArgumentNullException.ThrowIfNull(typeMap);
        this.grains = grains;
        this.catalog = catalog;
        this.payloads = payloads;
        this.typeMap = typeMap;
    }

    public GrainBehaviorCapabilityDispatchAccess(
        IGrainFactory grains,
        ActiveCapabilityCatalog catalog,
        IBehaviorProtectedPayloadAccess payloads)
        : this(grains, catalog, payloads, ResolveTypeMap(grains))
    {
    }

    public async ValueTask<ProtectedPayloadReference> DispatchAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        BehaviorCapabilityEdge edge,
        ProtectedPayloadReference requestPayload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edge);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(owner, task, attempt);
        var resolved = BehaviorCapabilityEdgeAuthority.ResolveExact(owner, edge, catalog, typeMap);

        var snapshot = await ReadAndValidateTaskAsync(owner, task, attempt, cancellationToken)
            .ConfigureAwait(false);
        var worker = snapshot.Worker;
        cancellationToken.ThrowIfCancellationRequested();

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
            var receipt = await broker
                .StageDispatch(task, attempt, edge, requestPayload, bound.Token)
                .ConfigureAwait(false);
            if (receipt.Worker != worker || receipt.Task != task)
            {
                throw new InvalidOperationException("worker-mismatch");
            }

            var responseSynapse = await PollJournalAsync(
                    session,
                    worker,
                    receipt.Correlation,
                    resolved.DeliveryTarget,
                    resolved.ResponseType,
                    cursor.ResumeSequence,
                    bound.Token)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var responseBytes = BehaviorPayloadJson.Serialize(responseSynapse, resolved.ResponseType);
            if (responseBytes.Length == 0)
            {
                throw new InvalidOperationException("empty-response-payload");
            }

            return await payloads
                .StoreAsync(owner, task, attempt, responseBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("operation-timeout");
        }
    }

    public async ValueTask<string> EmitFactAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        BehaviorId behavior,
        string emitAlias,
        string factJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emitAlias);
        ArgumentException.ThrowIfNullOrWhiteSpace(factJson);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(owner, task, attempt);
        behavior.EnsureValid();

        // The host process is never trusted for identity: the task must exist, be owner-scoped
        // and be on this exact attempt before its behavior is allowed to speak.
        var snapshot = await ReadAndValidateTaskAsync(owner, task, attempt, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot.Activation?.BehaviorId != behavior)
        {
            throw new NeuronAuthorizationException("behavior-activation-mismatch");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The grant itself is re-verified against the signed manifest inside EmitFact.
        return await grains
            .GetGrain<IBehaviorNeuron>(new NeuronId("behaviorneuron", owner, behavior.Value).ToGrainId())
            .EmitFact(new EmitBehaviorFact(
                EmitCommandId(task, attempt, emitAlias, factJson),
                emitAlias,
                factJson))
            .ConfigureAwait(false);
    }

    // A fresh id per request made every retried POST a second emission. The request itself is
    // the identity, so the same request reaches EmitFact under the same command and receipts.
    internal static CommandId EmitCommandId(
        NeuronId task,
        AttemptId attempt,
        string emitAlias,
        string factJson)
    {
        var material = Encoding.UTF8.GetBytes(
            $"{task}|{attempt.Value:N}|{emitAlias}|{factJson}");
        return new CommandId(new Guid(SHA256.HashData(material).AsSpan(0, 16)));
    }

    private static ActiveModuleContractTypeMap ResolveTypeMap(IGrainFactory grains)
    {
        if (grains is IClusterClient client)
        {
            return client.ServiceProvider.GetRequiredService<ActiveModuleContractTypeMap>();
        }

        if (grains is IServiceProvider services)
        {
            return services.GetRequiredService<ActiveModuleContractTypeMap>();
        }

        throw new InvalidOperationException("contract-type-map-unavailable");
    }

    private static void ValidateIdentity(OwnerId owner, NeuronId task, AttemptId attempt)
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
    }

    private async Task<TaskSnapshot> ReadAndValidateTaskAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authority = grains.GetGrain<IBehaviorTaskAuthority>(
            BehaviorTaskAuthority.ForOwner(owner).ToGrainId());
        return await authority
            .ReadValidatedTask(task, attempt, requireActivation: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<Synapse> PollJournalAsync(
        ISessionNeuron session,
        NeuronId worker,
        CorrelationId correlation,
        NeuronId expectedCaller,
        Type responseType,
        long afterSequence,
        CancellationToken cancellationToken)
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

            foreach (var delivery in page.Delta)
            {
                if (delivery.CorrelationId == correlation
                    && delivery.Caller == expectedCaller
                    && responseType.IsInstanceOfType(delivery.Synapse))
                {
                    return delivery.Synapse;
                }
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
}
