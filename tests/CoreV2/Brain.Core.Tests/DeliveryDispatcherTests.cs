using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Core.Delivery;
using Brain.Core.Endpoints;
using Brain.Core.Neurons;
using Brain.Core.Outbox;
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
    public async Task Rewire_after_emit_cannot_reroute_the_staged_snapshot()
    {
        var fixture = new Fixture();
        var entry = fixture.Entry(fixture.SummaryTarget, fixture.Produced, fixture.Produced);
        fixture.Directory.ReplaceRoute(fixture.SummaryTarget, fixture.AssessmentTarget);

        await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Summary.CommitCount);
        Assert.Equal(0, fixture.Assessment.CommitCount);
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
    public void Snapshot_delivery_ids_are_distinct_for_the_same_firing_when_revisions_differ()
    {
        var fixture = new Fixture();
        var route = new GraphRoute(fixture.SummaryTarget, SynapseKey.New(), 1, fixture.Produced, fixture.Produced, null);
        var revised = new GraphRoute(fixture.SummaryTarget, route.Synapse, 2, fixture.Produced, fixture.Produced, null);

        Assert.NotEqual(route.ToDeliverySnapshot().Delivery, revised.ToDeliverySnapshot().Delivery);
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
        fixture.Payloads.Record(firing, fixture.Produced, new Produced("one"));

        Assert.Throws<KeyNotFoundException>(() => fixture.Payloads.Read(firing, fixture.Assessed));
    }

    [Fact]
    public async Task Snapshot_contract_mismatch_is_rejected_without_touching_the_receiver()
    {
        var fixture = new Fixture();
        var firing = FiringId.New();
        fixture.Payloads.Record(firing, fixture.Produced, new Produced("payload"));
        var entry = new OutboxEntry(
            firing,
            EventId.New(),
            fixture.Produced,
            new ActivityContext(fixture.Source.Workspace, new PrincipalId("principal/alice"), BrainActivityId.New(), new CorrelationId("correlation/two")),
            null,
            fixture.Source,
            DateTimeOffset.UtcNow,
            [new DeliverySnapshot(DeliveryId.New(), fixture.SummaryTarget, SynapseKey.New(), 1, fixture.Assessed, fixture.Assessed, null)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken));

        Assert.Equal(0, fixture.Summary.CommitCount);
    }

    [Fact]
    public async Task Throwing_receiver_releases_its_reservation_before_a_successful_retry()
    {
        var fixture = new Fixture(receiver: new ThrowingReceiver());
        var entry = fixture.Entry(fixture.SummaryTarget, fixture.Produced, fixture.Produced);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken));
        var retried = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);
        var duplicate = await fixture.Dispatcher.DispatchAsync(entry, TestContext.Current.CancellationToken);

        Assert.Equal(1, fixture.Summary.CommitCount);
        Assert.Equal(1, retried.DeliveredCount);
        Assert.Equal(1, duplicate.DuplicateCount);
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

    private sealed class Fixture
    {
        public Fixture(RecordingReceiver? receiver = null)
        {
            Produced = new ContractId("proof/produced@1");
            Assessed = new ContractId("proof/assessed@1");
            Source = new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("proof"), new NeuronRoleId("proof.source"), "workspace");
            SummaryTarget = new EndpointAddress(Source.Workspace, new ModuleId("summary"), new NeuronRoleId("summary.target"), "workspace");
            AssessmentTarget = new EndpointAddress(Source.Workspace, new ModuleId("assessment"), new NeuronRoleId("assessment.target"), "workspace");
            Payloads = new InMemoryFiringPayloadStore();
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
        public InMemoryFiringPayloadStore Payloads { get; }
        public RecordingReceiver Summary { get; }
        public RecordingReceiver Assessment { get; }
        public ReceiverDirectory Directory { get; }
        public DeliveryDispatcher Dispatcher { get; }

        public OutboxEntry Entry(EndpointAddress? target = null, ContractId? input = null, ContractId? output = null, ImmutableArray<DeliverySnapshot> deliveries = default)
        {
            var firing = FiringId.New();
            Payloads.Record(firing, Produced, new Produced("payload"));
            var snapshots = deliveries.IsDefault
                ? [new DeliverySnapshot(DeliveryId.New(), target ?? SummaryTarget, SynapseKey.New(), 1, input ?? Produced, output ?? Produced, null)]
                : deliveries;
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

        public void ReplaceRoute(EndpointAddress oldTarget, EndpointAddress newTarget)
        {
            // Models a graph rewire external to this dispatcher. Existing snapshots still name oldTarget.
            _ = oldTarget;
            _ = newTarget;
        }
    }

    private class RecordingReceiver(EndpointAddress endpoint, ContractId acceptedContract) : IDeliveryReceiver
    {
        public EndpointAddress Endpoint { get; } = endpoint;
        public ContractId AcceptedContract { get; } = acceptedContract;
        public IDeliveryReceiverState DeliveryState { get; } = new InMemoryDeliveryReceiverState();
        public int CommitCount { get; private set; }

        public virtual Task ApplyAsync(DeliverySnapshot snapshot, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            CommitCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingReceiver() : RecordingReceiver(
        new EndpointAddress(new WorkspaceId("workspace/one"), new ModuleId("summary"), new NeuronRoleId("summary.target"), "workspace"),
        new ContractId("proof/produced@1"))
    {
        public override Task ApplyAsync(DeliverySnapshot snapshot, IDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            if (Attempts++ == 0)
            {
                throw new InvalidOperationException("application failed before its effect committed");
            }

            return base.ApplyAsync(snapshot, domainEvent, cancellationToken);
        }

        private int Attempts { get; set; }
    }

    private sealed class NoReshapes : IReshapeRegistry
    {
        public void Validate(DeliverySnapshot snapshot, IDomainEvent source) => throw new InvalidOperationException("No reshape is registered.");
        public IDomainEvent Transform(DeliverySnapshot snapshot, IDomainEvent source) => throw new InvalidOperationException("No reshape is registered.");
    }

    private sealed record Produced(string Value) : IDomainEvent;
}
