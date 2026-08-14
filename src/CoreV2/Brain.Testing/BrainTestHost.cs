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
using Brain.Abstractions.Wiring;
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
using Brain.Core.Wiring;
using Brain.Modules.Proof;
using Brain.Modules.Proof.Contracts;
using Brain.Testing.Fixtures;
using Orleans.TestingHost;
using TestCapability = Brain.Testing.Fakes.DeterministicCapability;
using TestCapabilityInput = Brain.Testing.Fakes.ProofCapabilityInput;

namespace Brain.Testing;

public sealed class BrainTestHost : IAsyncDisposable, IActivityPayloadReader, IProofRouteService, IProofActivityCompletion, IProofDeliveryPump
{
    private readonly WorkspaceFixture _callers = new();
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
    private readonly InMemoryFiringPayloadStore _payloads;
    private readonly ReshapeRegistry _reshapes;
    private readonly DeliveryDispatcher _deliveries;
    private readonly TestCapability _capability = new();
    private readonly CapabilityBroker _capabilities;
    private readonly OperationGateway _operations;
    private readonly TestCluster _cluster;

    private BrainTestHost(TestCluster cluster)
    {
        _cluster = cluster ?? throw new ArgumentNullException(nameof(cluster));
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

    public IOperationGateway Operations => _operations;

    public DeterministicTimeProvider Time { get; } = new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    public IReadOnlyList<string> RewireEvidence { get; private set; } = [];

    public static async Task<BrainTestHost> StartAsync()
    {
        var cluster = new TestClusterBuilder(1).Build();
        await cluster.DeployAsync();
        return new BrainTestHost(cluster);
    }

    public WorkspaceContext Caller(string workspace, string principal) => _callers.Caller(workspace, principal);

    public async Task<T> ReadResultAsync<T>(ActivityView view, WorkspaceContext caller)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(caller);
        if (view.Result is null)
        {
            throw new InvalidOperationException("The activity has no terminal result.");
        }

        return (await ReadResultAsync<T>(view.Result, caller, CancellationToken.None)).Value;
    }

    public Task<ActivityProgress<T>> ReadProgressAsync<T>(ActivityProgressReference reference, WorkspaceContext caller, CancellationToken cancellationToken)
        where T : class => throw new NotSupportedException("The proof has no progress payload.");

    public Task<ActivityResult<T>> ReadResultAsync<T>(ActivityResultReference reference, WorkspaceContext caller, CancellationToken cancellationToken)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _results.TryGetValue(reference.Payload.Value, out var value) && value is T typed
            ? Task.FromResult(new ActivityResult<T>(typed))
            : throw new KeyNotFoundException("No typed result is available for this activity reference.");
    }

    public ValueTask DisposeAsync() => _cluster.DisposeAsync();

    private async Task DispatchRunAsync(
        EndpointAddress endpoint,
        OperationInvocation<ProofInput> invocation,
        ActivityContext context,
        CancellationToken cancellationToken)
    {
        var source = Endpoint(ProofContracts.SourceRole, invocation.Caller);
        var resolver = new GraphResolver(_graph, _modules, _policy);
        var sourceNeuron = new ProofSourceNeuron(source, Store(source), resolver, this, Time);
        var entry = new ProofEntryNeuron(endpoint, Store(endpoint), resolver, _capabilities, this, sourceNeuron, Time);
        await entry.AcceptAsync(invocation.Input, context, cancellationToken);
    }

    private async Task<ProofCapabilityResult> InvokeClassifierAsync(ProofCapabilityInput input, CancellationToken cancellationToken)
    {
        var classified = await _capability.InvokeAsync(new TestCapabilityInput(input.Value), cancellationToken);
        return new ProofCapabilityResult(classified.Classification);
    }

    private async Task DispatchCorrectionAsync(
        EndpointAddress endpoint,
        OperationInvocation<CorrectionInput> invocation,
        ActivityContext context,
        CancellationToken cancellationToken)
    {
        var correction = new ProofCorrectionEntryNeuron(
            endpoint,
            Store(endpoint),
            new GraphResolver(_graph, _modules, _policy),
            this,
            this,
            Time);
        await correction.AcceptAsync(invocation.Input, context, cancellationToken);
    }

    async Task IProofRouteService.EnsureInitialAsync(ActivityContext context, CancellationToken cancellationToken)
        => await EnsureRouteAsync(context, "summary", cancellationToken);

    async Task<CorrectionResult> IProofRouteService.ReplaceAsync(ActivityContext context, string requestedRoute, CancellationToken cancellationToken)
    {
        await EnsureRouteAsync(context, requestedRoute, cancellationToken, replace: true);
        RewireEvidence = [.. RewireEvidence, "proof-rewire:" + requestedRoute];
        return new CorrectionResult(requestedRoute);
    }

    private async Task EnsureRouteAsync(ActivityContext context, string requestedRoute, CancellationToken cancellationToken, bool replace = false)
    {
        _ = cancellationToken;
        var caller = new WorkspaceContext(context.Workspace, context.Principal, false);
        var source = Endpoint(ProofContracts.SourceRole, caller);
        var target = Endpoint(requestedRoute == "summary" ? ProofContracts.SummaryRole : ProofContracts.AssessmentRole, caller);
        var key = context.Workspace.Value + "|" + context.Principal.Value;
        var reshape = requestedRoute == "assessment"
            ? new ReshapeDescriptor(ProofContracts.Produced, ProofContracts.Assessed, ProofContracts.Module)
            : null;
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

        var installed = await shard.InstallAsync(request);
        _routes[key] = installed.Key;
    }

    private EndpointAddress Endpoint(NeuronRoleId role, WorkspaceContext caller)
        => _endpoints.Resolve(_modules.Modules.SelectMany(static module => module.Roles).Single(candidate => candidate.Id == role), caller);

    private InMemoryOutboxStore<int> Store(EndpointAddress endpoint)
        => _stores.GetOrAdd(endpoint, _ => new InMemoryOutboxStore<int>(0));

    Task IProofActivityCompletion.CompleteAsync(ActivityContext context, object result, ContractId contract)
    {
        Complete(context.Activity, result, contract);
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

    private void Complete(BrainActivityId activity, object result, ContractId contract)
    {
        var reference = new ActivityPayloadReference("result/" + activity.Value.ToString("N"));
        _results[reference.Value] = result;
        new BrainActivityGrain(_activities, activity).Complete(new ActivityResultReference(contract, reference));
    }

    private Task CompleteDeliveryAsync(DeliverySnapshot snapshot, ProofResult result)
    {
        if (!_deliveryActivities.TryRemove(snapshot.Delivery, out var context))
        {
            throw new InvalidOperationException("No immutable activity context was associated with the proof delivery.");
        }

        return ((IProofActivityCompletion)this).CompleteAsync(context, result, ProofContracts.Result);
    }

    private static ReshapeId ReshapeIdForProducedAssessment()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{ProofContracts.Module.Value}|{ProofContracts.Produced.Value}|{ProofContracts.Assessed.Value}"));
        var value = new Guid(bytes.AsSpan(0, 16));
        return new ReshapeId(value == Guid.Empty ? new Guid(bytes.AsSpan(16, 16)) : value);
    }

    private sealed class EntryDispatcher(BrainTestHost host) : IEntryOperationDispatcher
    {
        public Task DispatchAsync<TInput>(EndpointAddress endpoint, OperationInvocation<TInput> invocation, ActivityContext context, CancellationToken cancellationToken)
            where TInput : class
            => invocation.Input switch
            {
                ProofInput input => host.DispatchRunAsync(endpoint, new OperationInvocation<ProofInput>(invocation.Operation, input, invocation.Caller, invocation.IdempotencyKey), context, cancellationToken),
                CorrectionInput input => host.DispatchCorrectionAsync(endpoint, new OperationInvocation<CorrectionInput>(invocation.Operation, input, invocation.Caller, invocation.IdempotencyKey), context, cancellationToken),
                _ => throw new InvalidOperationException("The proof host received an unregistered operation input."),
            };
    }

    private sealed class GraphResolver(GraphShardDirectory graph, ModuleSet modules, WorkspacePolicyEvaluator policy) : IGraphRouteResolver
    {
        public async Task<IReadOnlyList<GraphRoute>> ResolveAsync(EndpointAddress source, ContractId eventContract, ActivityContext activity, CancellationToken cancellationToken)
            => (await graph.Open(source, modules, policy).ResolveAsync(source, eventContract)).Deliveries
                .Select(static delivery => new GraphRoute(delivery.Target, delivery.SynapseKey, delivery.SynapseRevision, delivery.InputContract, delivery.OutputContract, delivery.Reshape))
                .ToArray();
    }

    private sealed class ReceiverDirectory(BrainTestHost host, ConcurrentDictionary<EndpointAddress, ProofReceiverNeuron> receivers) : IDeliveryReceiverDirectory
    {
        public IDeliveryReceiver Resolve(EndpointAddress target) => receivers.GetOrAdd(target, endpoint =>
        {
            var assessment = endpoint.Role == ProofContracts.AssessmentRole;
            return new ProofReceiverNeuron(endpoint, assessment ? ProofContracts.Assessed : ProofContracts.Produced, assessment ? "assessment" : "summary", host.CompleteDeliveryAsync);
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
