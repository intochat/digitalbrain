using DigitalBrain.Poc.Abstractions;
using DigitalBrain.Poc.Runtime;
using Xunit;

namespace DigitalBrain.Poc.Runtime.Tests;

public sealed class CandidateDeliveryFacts
{
    [Fact]
    public void TrustedInputFansOutOnlyToGrantedFamiliesAndPinsTheirCurrentRevisions()
    {
        var first = CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa");
        var second = CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb");
        var excluded = CandidateFamilyId.Parse("cf_cccccccccccccccccccccccccc");
        var firstIdentity = new CandidateModuleIdentity(
            new string('a', 64),
            new string('b', 64),
            new string('c', 64));
        var secondIdentity = new CandidateModuleIdentity(
            new string('d', 64),
            new string('e', 64),
            new string('f', 64));
        var excludedIdentity = new CandidateModuleIdentity(
            new string('1', 64),
            new string('2', 64),
            new string('3', 64));
        var routes = new ImmutableRouteTable(
        [
            RouteBinding.Candidate("owner-a", "db.poc.probe.ingress.v1", first, "r1", firstIdentity, "NeuronA"),
            RouteBinding.Candidate("owner-a", "db.poc.probe.ingress.v1", second, "r4", secondIdentity, "NeuronB"),
            RouteBinding.Candidate("owner-a", "db.poc.other.ingress.v1", excluded, "r2", excludedIdentity, "NeuronC"),
            RouteBinding.Candidate("owner-b", "db.poc.probe.ingress.v1", excluded, "r9", excludedIdentity, "NeuronD"),
        ]);

        var envelopes = routes.ExpandTrustedInput(
            "owner-a",
            "input-1",
            new ProbeIngress("probe"));

        Assert.Equal(2, envelopes.Count);
        Assert.Collection(
            envelopes.OrderBy(envelope => envelope.CandidateFamily!.Value.Value),
            envelope =>
            {
                Assert.Equal(first, envelope.CandidateFamily);
                Assert.Equal("r1", envelope.TargetRevision);
                Assert.Equal(firstIdentity, envelope.TargetModuleIdentity);
            },
            envelope =>
            {
                Assert.Equal(second, envelope.CandidateFamily);
                Assert.Equal("r4", envelope.TargetRevision);
                Assert.Equal(secondIdentity, envelope.TargetModuleIdentity);
            });
        Assert.Equal(2, envelopes.Select(envelope => envelope.DeliveryId).Distinct().Count());
    }

    [Fact]
    public void ExactHandlerCatalogRejectsSynapseBaseHandlerAndUnknownAlias()
    {
        Assert.Throws<InvalidOperationException>(
            () => ExactHandlerCatalog.Create([typeof(BaseHandlerNeuron)]));

        var catalog = ExactHandlerCatalog.Create([typeof(ProbeHandlerNeuron)]);
        Assert.Throws<UnknownSynapseAliasException>(
            () => catalog.Resolve("db.poc.unknown.v1"));
    }

    private sealed class BaseHandlerNeuron :
        Neuron,
        IHandle<Synapse>
    {
        public BaseHandlerNeuron()
            : base(NullBrain.Instance)
        {
        }

        public Task HandleAsync(Synapse synapse, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ProbeHandlerNeuron :
        Neuron,
        IHandle<ProbeIngress>
    {
        public ProbeHandlerNeuron()
            : base(NullBrain.Instance)
        {
        }

        public Task HandleAsync(ProbeIngress synapse, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NullBrain : IDigitalBrain
    {
        public static NullBrain Instance { get; } = new();

        public Task FireSynapse(Synapse synapse, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
