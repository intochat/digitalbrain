using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Kernel.Tests;

public class DomainRegistrarNeuronAggregationTests
{
    [Fact]
    public void Build_aggregates_DeclaredNeurons_across_all_domains()
    {
        var reg = DomainRegistrar.Build(new RegistrationOptions
        {
            Silo = DomainId.From("domains"),
            Domains = [new FakeDomainA(), new FakeDomainB()],
        });

        Assert.Equal(
            new[] { "a.verb", "b.verb" }.OrderBy(x => x),
            reg.Neurons.Select(e => e.Id.Value).OrderBy(x => x));
    }

    [Fact]
    public void Build_produces_empty_Neurons_when_domains_declare_none()
    {
        var reg = DomainRegistrar.Build(new RegistrationOptions
        {
            Silo = DomainId.From("domains"),
            Domains = [new DomainWithNoNeurons()],
        });

        Assert.Empty(reg.Neurons);
    }

    private sealed class FakeDomainA : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.A");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
        public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
        [
            new NeuronDefinition(NeuronId.From("a.verb"), "A verb", "desc",
                typeof(object), ["do a"]),
        ];
    }

    private sealed class FakeDomainB : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.B");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
        public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
        [
            new NeuronDefinition(NeuronId.From("b.verb"), "B verb", "desc",
                typeof(object), ["do b"]),
        ];
    }

    private sealed class DomainWithNoNeurons : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.Empty");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
    }
}
