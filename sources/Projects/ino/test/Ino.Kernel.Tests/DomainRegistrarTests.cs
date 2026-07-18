using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Kernel.Tests;

public class DomainRegistrarTests
{
    [Fact]
    public void Build_discovers_INeuron_implementations_as_canonical()
    {
        var exp = new FakeDomain();
        var result = DomainRegistrar.Build(new RegistrationOptions
        {
            Silo = DomainId.From("domains"),
            Domains = [exp],
        });

        Assert.Contains(result.Canonical, c =>
            c.SynapseType == typeof(FakeSynapse) && c.GrainType == typeof(HandlerA));
    }

    [Fact]
    public void Build_reads_PerGrainCapabilities_from_domain_neuron()
    {
        var exp = new FakeDomainWithCaps();
        var result = DomainRegistrar.Build(new RegistrationOptions
        {
            Silo = DomainId.From("domains"),
            Domains = [exp],
        });

        var entry = result.Canonical.Single(c => c.GrainType == typeof(HandlerA));
        var only = Assert.Single(entry.RequiredCapabilities);
        Assert.IsType<Capability.Llm>(only);
    }

    [Fact]
    public void Build_adds_built_in_grain_types_with_built_in_domain_id()
    {
        var result = DomainRegistrar.Build(new RegistrationOptions
        {
            Silo = DomainId.From("kernel"),
            Domains = [],
            BuiltInGrainTypes = [typeof(HandlerA)],
        });

        Assert.Equal("Ino.Kernel.BuiltIns", result.Canonical
            .Single(c => c.GrainType == typeof(HandlerA))
            .Domain.Value);
    }

    internal sealed record FakeSynapse : ISynapse;

    internal sealed class HandlerA : INeuron<FakeSynapse>
    {
        public Task<NeuronResult> HandleAsync(FakeSynapse synapse, NeuronContext ctx, CancellationToken ct)
            => Task.FromResult(NeuronResult.Ok());
    }

    private sealed class FakeDomain : IDomain
    {
        public DomainId Id => DomainId.From("fake");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
    }

    private sealed class FakeDomainWithCaps : IDomain
    {
        public DomainId Id => DomainId.From("fake-caps");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities =>
            [ new Capability.Llm(LlmTier.Reasoning) ];
        public IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities =>
            new Dictionary<Type, IReadOnlyList<Capability>>
            {
                [typeof(HandlerA)] = [ new Capability.Llm(LlmTier.Reasoning) ],
            };
    }
}
