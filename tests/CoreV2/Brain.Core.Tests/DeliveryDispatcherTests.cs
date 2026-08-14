using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Policy;
using Brain.Core.Delivery;
using Brain.Core.Endpoints;
using Brain.Core.Graph;
using Brain.Core.Modules;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
using Brain.Core.Policy;
using Brain.Core.Reshapes;
using Xunit;

namespace Brain.Core.Tests;

public sealed class DeliveryDispatcherTests
{
    [Fact]
    public async Task Duplicate_delivery_id_commits_receiver_effect_once()
    {
        var fixture = new Fixture();
        var entry = fixture.Entry(fixture.SummaryTarget, fixture.Produced, fixture.Produced);

        await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);
        var duplicate = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Summary.CommitCount);
        Assert.Equal(1, duplicate.DuplicateCount);
    }

    [Fact]
    public async Task Zero_route_has_no_receiver_effect_and_never_creates_a_refusal()
    {
        var fixture = new Fixture();
        var entry = fixture.Entry(deliveries: []);

        var result = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.DeliveredCount);
        Assert.False(result.CreatedRefusal);
    }

    [Fact]
    public void Snapshot_delivery_id_is_stable_for_the_same_firing_and_route_revision()
    {
        var fixture = new Fixture();
        var firing = FiringId.New();
        var route = new GraphRoute(fixture.SummaryTarget, SynapseKey.New(), 1, fixture.Produced, fixture.Produced, null);

        Assert.Equal(route.ToDeliverySnapshot(firing).Delivery, route.ToDeliverySnapshot(firing).Delivery);
    }

    [Fact]
    public void Snapshot_delivery_id_changes_when_firing_synapse_or_revision_changes()
    {
        var fixture = new Fixture();
        var firing = FiringId.New();
        var route = new GraphRoute(fixture.SummaryTarget, SynapseKey.New(), 1, fixture.Produced, fixture.Produced, null);
        var revised = new GraphRoute(fixture.SummaryTarget, route.Synapse, 2, fixture.Produced, fixture.Produced, null);
        var otherSynapse = new GraphRoute(fixture.SummaryTarget, SynapseKey.New(), 1, fixture.Produced, fixture.Produced, null);

        var original = route.ToDeliverySnapshot(firing).Delivery;
        Assert.NotEqual(original, revised.ToDeliverySnapshot(firing).Delivery);
        Assert.NotEqual(original, otherSynapse.ToDeliverySnapshot(firing).Delivery);
        Assert.NotEqual(original, route.ToDeliverySnapshot(FiringId.New()).Delivery);
    }

    [Fact]
    public void Outbox_rejects_a_delivery_id_that_is_not_derived_from_its_firing_and_snapshot()
    {
        var fixture = new Fixture();
        var firing = FiringId.New();
        var synapse = SynapseKey.New();
        var delivery = new DeliverySnapshot(
            new DeliveryId(Guid.NewGuid()),
            fixture.SummaryTarget,
            synapse,
            1,
            fixture.Produced,
            fixture.Produced,
            null);

        Assert.Throws<ArgumentException>(() => new OutboxEntry(
            firing,
            EventId.New(),
            fixture.Produced,
            new ActivityContext(fixture.Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/invalid-delivery")),
            null,
            fixture.Source,
            DateTimeOffset.UtcNow,
            [delivery]));
    }

    [Fact]
    public void Dispatcher_does_not_depend_on_a_graph_route_resolver()
    {
        var source = File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../")),
            "src", "CoreV2", "Brain.Core", "Delivery", "DeliveryDispatcher.cs"));

        Assert.DoesNotContain("IGraphRouteResolver", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Brain.Core.Graph", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Payload_store_does_not_return_an_event_under_a_different_contract()
    {
        var fixture = new Fixture();
        var firing = FiringId.New();
        fixture.Payloads.Record(firing, fixture.Produced, fixture.Source, new Produced("one"));

        Assert.Throws<KeyNotFoundException>(() => fixture.Payloads.Read(firing, fixture.Assessed));
    }

    [Fact]
    public void Payload_store_rejects_an_event_clr_type_that_does_not_match_its_declared_contract()
    {
        var fixture = new Fixture();

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Payloads.Record(FiringId.New(), fixture.Assessed, fixture.Source, new Produced("wrong-contract")));
    }

    [Fact]
    public async Task Snapshot_contract_mismatch_is_rejected_without_touching_the_receiver()
    {
        var fixture = new Fixture();
        var firing = FiringId.New();
        fixture.Payloads.Record(firing, fixture.Produced, fixture.Source, new Produced("payload"));
        var synapse = SynapseKey.New();
        var entry = new OutboxEntry(
            firing,
            EventId.New(),
            fixture.Produced,
            new ActivityContext(fixture.Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/two")),
            null,
            fixture.Source,
            DateTimeOffset.UtcNow,
            [new DeliverySnapshot(DeliveryId.Derive(firing, synapse, 1), fixture.SummaryTarget, synapse, 1, fixture.Assessed, fixture.Assessed, null)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.Summary.CommitCount);
    }

    [Fact]
    public async Task Throwing_receiver_discards_its_private_candidate_and_receipt_before_retry()
    {
        var fixture = new Fixture(receiver: new ThrowingReceiver());
        var entry = fixture.Entry(fixture.SummaryTarget, fixture.Produced, fixture.Produced);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Summary.CommitCount);
        Assert.Equal(0, fixture.Summary.CompletedReceiptCount);
        Assert.Equal(1, fixture.Summary.StageCount);
        var retried = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);
        var duplicate = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Summary.CommitCount);
        Assert.Equal(1, fixture.Summary.CompletedReceiptCount);
        Assert.Equal(2, fixture.Summary.StageCount);
        Assert.Equal(1, retried.DeliveredCount);
        Assert.Equal(1, duplicate.DuplicateCount);
    }

    [Fact]
    public async Task Concurrent_duplicate_waits_for_the_receiver_owned_transaction_then_reports_the_committed_duplicate()
    {
        var receiver = new BlockingReceiver();
        var fixture = new Fixture(receiver);
        var entry = fixture.Entry(fixture.SummaryTarget, fixture.Produced, fixture.Produced);

        var first = fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);
        await receiver.ApplicationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var duplicate = fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.False(duplicate.IsCompleted);
        receiver.ReleaseApplication.SetResult();
        var firstResult = await first;
        var duplicateResult = await duplicate;

        Assert.Equal(1, firstResult.DeliveredCount);
        Assert.Equal(1, duplicateResult.DuplicateCount);
        Assert.Equal(1, fixture.Summary.CommitCount);
    }

    [Fact]
    public async Task Real_graph_rewire_after_staging_does_not_change_the_captured_receiver()
    {
        var fixture = new GraphRewireFixture();
        var installed = await fixture.Graph.InstallAsync(fixture.Request);
        var routes = (await fixture.Graph.ResolveAsync(fixture.Source, fixture.Produced)).Deliveries
            .Select(route => new GraphRoute(route.Target, route.SynapseKey, route.SynapseRevision, route.InputContract, route.OutputContract, route.Reshape))
            .ToArray();
        var turn = new NeuronTurn<int>(new NeuronStateSnapshot<int>(0, 0), fixture.Source, fixture.Context, TimeProvider.System);
        turn.StageEmission(fixture.Produced, routes);
        var entry = Assert.Single(turn.Commit().Emissions);
        fixture.Payloads.Record(entry.Firing, fixture.Produced, fixture.Source, new Produced("payload"));

        await fixture.Graph.ReplaceAsync(installed.Key, fixture.Request with { Target = fixture.AssessmentTarget });
        var result = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.DeliveredCount);
        Assert.Equal(1, fixture.Summary.CommitCount);
        Assert.Equal(0, fixture.Assessment.CommitCount);
    }

    [Fact]
    public void Runtime_delivery_records_and_interfaces_have_no_untyped_or_provider_payload_escape_hatches()
    {
        var runtime = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../")), "src", "CoreV2", "Brain.Core");
        var files = new[]
        {
            Path.Combine(runtime, "Delivery", "DeliveryDispatcher.cs"),
            Path.Combine(runtime, "Delivery", "DeliveryDeduplicator.cs"),
            Path.Combine(runtime, "Reshapes", "ReshapeRegistry.cs"),
            Path.Combine(runtime, "Outbox", "OutboxEntry.cs"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("JsonElement", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Text.Json", source, StringComparison.Ordinal);
            Assert.DoesNotContain(" provider", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("object", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Entity", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Receiver_transaction_uses_copy_on_write_without_lifecycle_marker_choreography()
    {
        var source = File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../")),
            "src", "CoreV2", "Brain.Core", "Delivery", "DeliveryDeduplicator.cs"));

        Assert.DoesNotContain("TryBegin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Complete(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Abandon(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Func<", source, StringComparison.Ordinal);
        Assert.Contains("ReceiverDeliveryState", source, StringComparison.Ordinal);
    }

    private sealed class Fixture
    {
        public Fixture(RecordingReceiver? receiver = null)
        {
            Produced = new ContractId("proof/produced@1");
            Assessed = new ContractId("proof/assessed@1");
            Source = new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("proof"), new NeuronRoleId("proof.source"), "workspace");
            SummaryTarget = new EndpointAddress(Source.Workspace, new ModuleId("summary"), new NeuronRoleId("summary.target"), "workspace");
            AssessmentTarget = new EndpointAddress(Source.Workspace, new ModuleId("assessment"), new NeuronRoleId("assessment.target"), "workspace");
            Modules = ManifestValidator.Validate(
            [
                new ModuleManifest(Source.Module, new ModuleVersion(1, 0, 0), [], [], [],
                    [new EventDescriptor(Produced, Source.Module, typeof(Produced), EventVisibility.Published), new EventDescriptor(Assessed, Source.Module, typeof(Assessed), EventVisibility.Published)],
                    [Produced, Assessed], [], [], []),
            ]);
            Payloads = new InMemoryFiringPayloadStore(Modules);
            Summary = receiver ?? new RecordingReceiver(SummaryTarget, Produced);
            Assessment = new RecordingReceiver(AssessmentTarget, Produced);
            Directory = new ReceiverDirectory(Summary, Assessment);
            Dispatcher = new DeliveryDispatcher(Payloads, Directory, new NoReshapes());
        }

        public ContractId Produced { get; }
        public ContractId Assessed { get; }
        public EndpointAddress Source { get; }
        public EndpointAddress SummaryTarget { get; }
        public EndpointAddress AssessmentTarget { get; }
        public ModuleSet Modules { get; }
        public InMemoryFiringPayloadStore Payloads { get; }
        public RecordingReceiver Summary { get; }
        public RecordingReceiver Assessment { get; }
        public ReceiverDirectory Directory { get; }
        public DeliveryDispatcher Dispatcher { get; }

        public OutboxEntry Entry(EndpointAddress? target = null, ContractId? input = null, ContractId? output = null, ImmutableArray<DeliverySnapshot> deliveries = default)
        {
            var firing = FiringId.New();
            Payloads.Record(firing, Produced, Source, new Produced("payload"));
            ImmutableArray<DeliverySnapshot> snapshots;
            if (deliveries.IsDefault)
            {
                var synapse = SynapseKey.New();
                snapshots = [new DeliverySnapshot(
                    DeliveryId.Derive(firing, synapse, 1),
                    target ?? SummaryTarget,
                    synapse,
                    1,
                    input ?? Produced,
                    output ?? Produced,
                    null)];
            }
            else
            {
                snapshots = deliveries;
            }
            return new OutboxEntry(
                firing,
                EventId.New(),
                Produced,
                new ActivityContext(Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/one")),
                null,
                Source,
                DateTimeOffset.UtcNow,
                snapshots);
        }
    }

    private sealed class ReceiverDirectory(params RecordingReceiver[] receivers) : IDeliveryReceiverDirectory
    {
        private readonly Dictionary<EndpointAddress, RecordingReceiver> _receivers = receivers.ToDictionary(receiver => receiver.Endpoint);

        public IDeliveryReceiver Resolve(EndpointAddress target)
            => _receivers.TryGetValue(target, out var receiver) ? receiver : throw new KeyNotFoundException();

    }

    private class RecordingReceiver(EndpointAddress endpoint, ContractId acceptedContract)
        : IDeliveryReceiver, IReceiverDeliveryHandler<ReceiverState>
    {
        private readonly InMemoryReceiverDeliveryStore<ReceiverState> _store = new(new ReceiverState(0));

        public EndpointAddress Endpoint { get; } = endpoint;
        public ContractId AcceptedContract { get; } = acceptedContract;
        public int CommitCount => _store.State.CommitCount;
        public int CompletedReceiptCount => _store.CompletedReceiptCount;
        public int StageCount { get; private set; }

        public Task<ReceiverDeliveryResult> DeliverAsync(DeliverySnapshot snapshot, IDomainEvent domainEvent, CancellationToken cancellationToken)
            => _store.DeliverAsync(snapshot, domainEvent, this, cancellationToken);

        public virtual Task<ReceiverState> StageAsync(
            ReceiverState candidate,
            DeliverySnapshot snapshot,
            IDomainEvent domainEvent,
            CancellationToken cancellationToken)
        {
            StageCount++;
            return Task.FromResult(candidate with { CommitCount = candidate.CommitCount + 1 });
        }

        protected void CountStage() => StageCount++;
    }

    private sealed class ThrowingReceiver() : RecordingReceiver(
        new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("summary"), new NeuronRoleId("summary.target"), "workspace"),
        new ContractId("proof/produced@1"))
    {
        public override Task<ReceiverState> StageAsync(
            ReceiverState candidate,
            DeliverySnapshot snapshot,
            IDomainEvent domainEvent,
            CancellationToken cancellationToken)
        {
            CountStage();
            if (Attempts++ == 0)
            {
                var privateCandidate = candidate with { CommitCount = candidate.CommitCount + 1 };
                _ = privateCandidate;
                throw new InvalidOperationException("private candidate staging failed before receiver commit");
            }

            return Task.FromResult(candidate with { CommitCount = candidate.CommitCount + 1 });
        }

        private int Attempts { get; set; }
    }

    private sealed class BlockingReceiver() : RecordingReceiver(
        new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("summary"), new NeuronRoleId("summary.target"), "workspace"),
        new ContractId("proof/produced@1"))
    {
        public TaskCompletionSource ApplicationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseApplication { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<ReceiverState> StageAsync(
            ReceiverState candidate,
            DeliverySnapshot snapshot,
            IDomainEvent domainEvent,
            CancellationToken cancellationToken)
        {
            CountStage();
            ApplicationStarted.SetResult();
            await ReleaseApplication.Task.WaitAsync(cancellationToken);
            return candidate with { CommitCount = candidate.CommitCount + 1 };
        }
    }

    private sealed class NoReshapes : IReshapeRegistry
    {
        public void Validate(DeliverySnapshot snapshot, IDomainEvent source) => throw new InvalidOperationException("No reshape is registered.");
        public IDomainEvent Transform(DeliverySnapshot snapshot, IDomainEvent source) => throw new InvalidOperationException("No reshape is registered.");
    }

    private sealed record Produced(string Value) : IDomainEvent;
    private sealed record Assessed(string Value) : IDomainEvent;
    private sealed record ReceiverState(int CommitCount);

    private sealed class GraphRewireFixture
    {
        public GraphRewireFixture()
        {
            Produced = new ContractId("proof/produced@1");
            Source = new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("proof"), new NeuronRoleId("proof.source"), "workspace");
            SummaryTarget = new EndpointAddress(Source.Workspace, new ModuleId("summary"), new NeuronRoleId("summary.target"), "workspace");
            AssessmentTarget = new EndpointAddress(Source.Workspace, new ModuleId("assessment"), new NeuronRoleId("assessment.target"), "workspace");
            Context = new ActivityContext(Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/graph"));
            var modules = ManifestValidator.Validate(
            [
                new ModuleManifest(Source.Module, new ModuleVersion(1, 0, 0), [], [new NeuronRoleDescriptor(Source.Role, NeuronScope.Workspace, Source.Module)], [], [new EventDescriptor(Produced, Source.Module, typeof(Produced), EventVisibility.Published)], [Produced], [], [], []),
                new ModuleManifest(SummaryTarget.Module, new ModuleVersion(1, 0, 0), [], [new NeuronRoleDescriptor(SummaryTarget.Role, NeuronScope.Workspace, SummaryTarget.Module)], [], [], [Produced], [], [], []),
                new ModuleManifest(AssessmentTarget.Module, new ModuleVersion(1, 0, 0), [], [new NeuronRoleDescriptor(AssessmentTarget.Role, NeuronScope.Workspace, AssessmentTarget.Module)], [], [], [Produced], [], [], []),
            ]);
            Graph = new GraphShardDirectory(new GraphShardResolver()).Open(Source, modules, new AllowGraphChanges());
            Request = new SynapseChangeRequest(Source, Produced, SummaryTarget, "workspace", new WiringSlotId("proof-produced"), null, Context);
            Payloads = new InMemoryFiringPayloadStore(modules);
            Summary = new RecordingReceiver(SummaryTarget, Produced);
            Assessment = new RecordingReceiver(AssessmentTarget, Produced);
            Dispatcher = new DeliveryDispatcher(Payloads, new ReceiverDirectory(Summary, Assessment), new NoReshapes());
        }

        public ContractId Produced { get; }
        public EndpointAddress Source { get; }
        public EndpointAddress SummaryTarget { get; }
        public EndpointAddress AssessmentTarget { get; }
        public ActivityContext Context { get; }
        public BrainGraphShardGrain Graph { get; }
        public SynapseChangeRequest Request { get; }
        public InMemoryFiringPayloadStore Payloads { get; }
        public RecordingReceiver Summary { get; }
        public RecordingReceiver Assessment { get; }
        public DeliveryDispatcher Dispatcher { get; }
    }

    private sealed class AllowGraphChanges : IWorkspacePolicyEvaluator
    {
        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, Brain.Abstractions.Operations.OperationDescriptor operation)
            => PolicyDecision.Allowed;

        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request)
            => PolicyDecision.Allowed;

        public PolicyDecision AuthorizeCapability(ActivityContext context, Brain.Abstractions.Capabilities.CapabilityDescriptor capability)
            => PolicyDecision.Refused;
    }
}
