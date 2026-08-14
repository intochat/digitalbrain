using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Activities;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Reshapes;
using Brain.Core.Activities;
using Brain.Core.Capabilities;
using Brain.Core.Delivery;
using Brain.Core.Endpoints;
using Brain.Core.Graph;
using Brain.Core.Modules;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Brain.Core.Policy;
using Brain.Core.Reshapes;
using Brain.Modules.Proof;
using Brain.Modules.Proof.Contracts;
using Brain.Testing.Fixtures;
using TestCapability = Brain.Testing.Fakes.DeterministicCapability;
using TestCapabilityInput = Brain.Testing.Fakes.ProofCapabilityInput;

namespace Brain.Testing;

public sealed class ProofRuntime : IProofRouteService, IProofActivityCompletion, IProofDeliveryPump
{
    private readonly ModuleSet _modules;
    private readonly EndpointResolver _endpoints;
    private readonly WorkspacePolicyEvaluator _policy;
    private readonly GraphShardDirectory _graph = new(new GraphShardResolver());
    private readonly InMemoryActivityStore _activities = new();
    private readonly ConcurrentDictionary<string, object> _results = new();
    private readonly ConcurrentDictionary<EndpointAddress, ProofReceiverNeuron> _receivers = new();
    private readonly ConcurrentDictionary<EndpointAddress, InMemoryOutboxStore<int>> _stores = new();
    private readonly ConcurrentDictionary<string, SynapseKey> _routes = new();
    private readonly ConcurrentDictionary<DeliveryId, ActivityContext> _deliveryActivities = new();
    private readonly ConcurrentQueue<string> _rewireEvidence = new();
    private readonly InMemoryFiringPayloadStore _payloads;
    private readonly ReshapeRegistry _reshapes;
    private readonly DeliveryDispatcher _deliveries;
    private readonly TestCapability _capability = new();
    private readonly CapabilityBroker _capabilities;
    private readonly OperationGateway _operations;
    private readonly DeterministicTimeProvider _time;
    private int _dispatchCount;

    public ProofRuntime(DeterministicTimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        var classifierModule = new ModuleManifest(
            new ModuleId("proof.classifier"), new ModuleVersion(1, 0, 0), [], [], [], [], [], [],
            [ProofContracts.ClassifierCapability], []);
        _modules = ManifestValidator.Validate([ProofManifest.Create(), classifierModule]);
        var registry = new ModuleRegistry();
        registry.Resolve(_modules.Modules);
        _endpoints = new EndpointResolver(_modules);
        _policy = new WorkspacePolicyEvaluator(_modules);
        _payloads = new InMemoryFiringPayloadStore(_modules);
        _reshapes = new ReshapeRegistry(_modules);
        _reshapes.Register(ReshapeIdForProducedAssessment(), new ReshapeDescriptor(ProofContracts.Produced, ProofContracts.Assessed, ProofContracts.Module), new ProofToAssessmentReshape());
        _deliveries = new DeliveryDispatcher(_payloads, new ReceiverDirectory(this, _receivers), _reshapes);
        _capabilities = new CapabilityBroker(
            registry,
            _policy,
            new CapabilityBindingResolver([CapabilityBinding.For<ProofCapabilityInput, ProofCapabilityResult>(ProofContracts.ClassifierCapability, InvokeClassifierAsync)]),
            new CapabilityUseState());
        _operations = new OperationGateway(
            registry,
            _policy,
            _endpoints,
            new EntryDispatcher(this),
            _activities,
            new ActivityProjectionService(_activities),
            new OperationTypeBindings([
                OperationTypeBinding.For<ProofInput, ProofResult>(ProofContracts.Run, new ProofInputCanonicalizer()),
                OperationTypeBinding.For<CorrectionInput, CorrectionResult>(ProofContracts.Correct, new CorrectionInputCanonicalizer()),
            ]));
    }

    internal Guid InstanceId { get; } = Guid.NewGuid();

    internal int DispatchCount => Volatile.Read(ref _dispatchCount);

    internal IReadOnlyList<string> RewireEvidence => _rewireEvidence.ToArray();

    internal Task<OperationAccepted> InvokeRunAsync(string value, string workspace, string principal, string key)
        => _operations.InvokeAsync<ProofInput, ProofResult>(ProofContracts.Run, new ProofInput(value), Caller(workspace, principal), new IdempotencyKey(key), CancellationToken.None);

    internal Task<OperationAccepted> InvokeCorrectionAsync(string requestedRoute, string workspace, string principal, string key)
        => _operations.InvokeAsync<CorrectionInput, CorrectionResult>(ProofContracts.Correct, new CorrectionInput(requestedRoute), Caller(workspace, principal), new IdempotencyKey(key), CancellationToken.None);

    internal Task<ActivityView> ObserveAsync(string activity, string workspace, string principal)
        => _operations.ObserveAsync(new BrainActivityId(Guid.Parse(activity)), Caller(workspace, principal), CancellationToken.None);

    internal async Task<ProofResult> ReadProofResultAsync(string payload, string workspace, string principal)
    {
        _ = Caller(workspace, principal);
        return (await ReadResultAsync<ProofResult>(new ActivityResultReference(ProofContracts.Result, new ActivityPayloadReference(payload)), CancellationToken.None)).Value;
    }

    internal async Task<CorrectionResult> ReadCorrectionResultAsync(string payload, string workspace, string principal)
    {
        _ = Caller(workspace, principal);
        return (await ReadResultAsync<CorrectionResult>(new ActivityResultReference(ProofContracts.CorrectionResult, new ActivityPayloadReference(payload)), CancellationToken.None)).Value;
    }

    private Task<ActivityResult<T>> ReadResultAsync<T>(ActivityResultReference reference, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _results.TryGetValue(reference.Payload.Value, out var value) && value is T typed
            ? Task.FromResult(new ActivityResult<T>(typed))
            : throw new KeyNotFoundException("No typed result is available for this activity reference.");
    }

    private async Task DispatchRunAsync(EndpointAddress endpoint, OperationInvocation<ProofInput> invocation, ActivityContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dispatchCount);
        var source = Endpoint(ProofContracts.SourceRole, invocation.Caller);
        var resolver = new GraphResolver(_graph, _modules, _policy);
        var sourceNeuron = new ProofSourceNeuron(source, Store(source), resolver, this, _time);
        var entry = new ProofEntryNeuron(endpoint, Store(endpoint), resolver, _capabilities, this, sourceNeuron, _time);
        await entry.AcceptAsync(invocation.Input, context, cancellationToken);
    }

    private async Task DispatchCorrectionAsync(EndpointAddress endpoint, OperationInvocation<CorrectionInput> invocation, ActivityContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _dispatchCount);
        var correction = new ProofCorrectionEntryNeuron(endpoint, Store(endpoint), new GraphResolver(_graph, _modules, _policy), this, this, _time);
        await correction.AcceptAsync(invocation.Input, context, cancellationToken);
    }

    private async Task<ProofCapabilityResult> InvokeClassifierAsync(ProofCapabilityInput input, CancellationToken cancellationToken)
    {
        var classified = await _capability.InvokeAsync(new TestCapabilityInput(input.Value), cancellationToken);
        return new ProofCapabilityResult(classified.Classification);
    }

    async Task IProofRouteService.EnsureInitialAsync(ActivityContext context, CancellationToken cancellationToken)
        => await EnsureRouteAsync(context, "summary", cancellationToken);

    async Task<CorrectionResult> IProofRouteService.ReplaceAsync(ActivityContext context, string requestedRoute, CancellationToken cancellationToken)
    {
        await EnsureRouteAsync(context, requestedRoute, cancellationToken, replace: true);
        _rewireEvidence.Enqueue("proof-rewire:" + requestedRoute);
        return new CorrectionResult(requestedRoute);
    }

    private async Task EnsureRouteAsync(ActivityContext context, string requestedRoute, CancellationToken cancellationToken, bool replace = false)
    {
        _ = cancellationToken;
        var caller = new WorkspaceContext(context.Workspace, context.Principal, false);
        var source = Endpoint(ProofContracts.SourceRole, caller);
        var target = Endpoint(requestedRoute == "summary" ? ProofContracts.SummaryRole : ProofContracts.AssessmentRole, caller);
        var key = context.Workspace.Value + "|" + context.Principal.Value;
        var reshape = requestedRoute == "assessment" ? new ReshapeDescriptor(ProofContracts.Produced, ProofContracts.Assessed, ProofContracts.Module) : null;
        var request = new SynapseChangeRequest(source, ProofContracts.Produced, target, "proof", new WiringSlotId("result"), reshape, context);
        var shard = _graph.Open(source, _modules, _policy);
        if (replace && _routes.TryGetValue(key, out var existing))
        {
            await shard.ReplaceAsync(existing, request);
            return;
        }

        if (_routes.ContainsKey(key))
        {
            return;
        }

        _routes[key] = (await shard.InstallAsync(request)).Key;
    }

    Task IProofActivityCompletion.CompleteAsync(ActivityContext context, object result, ContractId contract)
    {
        var reference = new ActivityPayloadReference("result/" + context.Activity.Value.ToString("N"));
        _results[reference.Value] = result;
        new BrainActivityGrain(_activities, context.Activity).Complete(new ActivityResultReference(contract, reference));
        return Task.CompletedTask;
    }

    async Task IProofDeliveryPump.DispatchAsync(OutboxEntry entry, ProofProduced payload, CancellationToken cancellationToken)
    {
        _payloads.Record(entry.Firing, entry.EventContract, entry.Source, payload);
        foreach (var snapshot in entry.Deliveries)
        {
            if (!_deliveryActivities.TryAdd(snapshot.Delivery, entry.Activity))
            {
                throw new InvalidOperationException("A proof delivery must preserve one immutable activity identity.");
            }
        }

        await _deliveries.DispatchAsync(entry, cancellationToken);
    }

    private Task CompleteDeliveryAsync(DeliverySnapshot snapshot, ProofResult result)
    {
        if (!_deliveryActivities.TryRemove(snapshot.Delivery, out var context))
        {
            throw new InvalidOperationException("No immutable activity context was associated with the proof delivery.");
        }

        return ((IProofActivityCompletion)this).CompleteAsync(context, result, ProofContracts.Result);
    }

    private EndpointAddress Endpoint(NeuronRoleId role, WorkspaceContext caller)
        => _endpoints.Resolve(_modules.Modules.SelectMany(static module => module.Roles).Single(candidate => candidate.Id == role), caller);

    private InMemoryOutboxStore<int> Store(EndpointAddress endpoint)
        => _stores.GetOrAdd(endpoint, _ => new InMemoryOutboxStore<int>(0));

    private static WorkspaceContext Caller(string workspace, string principal)
        => new(new WorkspaceId(workspace), new PrincipalId(principal), isServicePrincipal: false);

    private static ReshapeId ReshapeIdForProducedAssessment()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{ProofContracts.Module.Value}|{ProofContracts.Produced.Value}|{ProofContracts.Assessed.Value}"));
        var value = new Guid(bytes.AsSpan(0, 16));
        return new ReshapeId(value == Guid.Empty ? new Guid(bytes.AsSpan(16, 16)) : value);
    }

    private sealed class EntryDispatcher(ProofRuntime runtime) : IEntryOperationDispatcher
    {
        public Task DispatchAsync<TInput>(EndpointAddress endpoint, OperationInvocation<TInput> invocation, ActivityContext context, CancellationToken cancellationToken)
            where TInput : class
            => invocation.Input switch
            {
                ProofInput input => runtime.DispatchRunAsync(endpoint, new OperationInvocation<ProofInput>(invocation.Operation, input, invocation.Caller, invocation.IdempotencyKey), context, cancellationToken),
                CorrectionInput input => runtime.DispatchCorrectionAsync(endpoint, new OperationInvocation<CorrectionInput>(invocation.Operation, input, invocation.Caller, invocation.IdempotencyKey), context, cancellationToken),
                _ => throw new InvalidOperationException("The proof runtime received an unregistered operation input."),
            };
    }

    private sealed class GraphResolver(GraphShardDirectory graph, ModuleSet modules, WorkspacePolicyEvaluator policy) : IGraphRouteResolver
    {
        public async Task<IReadOnlyList<GraphRoute>> ResolveAsync(EndpointAddress source, ContractId eventContract, ActivityContext activity, CancellationToken cancellationToken)
            => (await graph.Open(source, modules, policy).ResolveAsync(source, eventContract)).Deliveries
                .Select(static delivery => new GraphRoute(delivery.Target, delivery.SynapseKey, delivery.SynapseRevision, delivery.InputContract, delivery.OutputContract, delivery.Reshape))
                .ToArray();
    }

    private sealed class ReceiverDirectory(ProofRuntime runtime, ConcurrentDictionary<EndpointAddress, ProofReceiverNeuron> receivers) : IDeliveryReceiverDirectory
    {
        public IDeliveryReceiver Resolve(EndpointAddress target) => receivers.GetOrAdd(target, endpoint =>
        {
            var assessment = endpoint.Role == ProofContracts.AssessmentRole;
            return new ProofReceiverNeuron(endpoint, assessment ? ProofContracts.Assessed : ProofContracts.Produced, assessment ? "assessment" : "summary", runtime.CompleteDeliveryAsync);
        });
    }

    private sealed class ProofInputCanonicalizer : IIdempotencyInputCanonicalizer<ProofInput>
    {
        public string Canonicalize(ProofInput input) => input.Value;
    }

    private sealed class CorrectionInputCanonicalizer : IIdempotencyInputCanonicalizer<CorrectionInput>
    {
        public string Canonicalize(CorrectionInput input) => input.RequestedRoute;
    }
}
