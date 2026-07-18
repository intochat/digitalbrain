using System.Collections.Immutable;
using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Tests;

public class IDomainTests
{
    [Fact]
    public void Default_PerGrainCapabilities_is_empty()
    {
        IDomain exp = new DefaultShapeDomain();
        Assert.Empty(exp.PerGrainCapabilities);
    }

    [Fact]
    public void Domain_declares_id_and_version()
    {
        IDomain exp = new DefaultShapeDomain();
        Assert.Equal(DomainId.From("Ino.Testing.Default"), exp.Id);
        Assert.Equal("1.0.0", exp.Version);
    }

    [Fact]
    public void PerGrainCapabilities_subset_rule_can_be_asserted_from_IDomain_alone()
    {
        IDomain exp = new DomainWithPerGrain();
        var allPerGrain = exp.PerGrainCapabilities.Values.SelectMany(x => x).Distinct();
        // tests can verify this invariant without Orleans, DI, or reflection
        Assert.All(allPerGrain, item => Assert.Contains(item, exp.DeclaredCapabilities));
    }

    [Fact]
    public void CanonicalTarget_holds_Type_not_string()
    {
        var target = new CanonicalTarget(
            SynapseType: typeof(string),
            GrainType: typeof(object),
            Domain: DomainId.From("x"),
            RequiredCapabilities: []);
        Assert.Equal(typeof(string), target.SynapseType);
        Assert.Equal(typeof(object), target.GrainType);
    }

    private sealed class DefaultShapeDomain : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.Default");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
    }

    [Fact]
    public void Default_DeclaredNeurons_is_empty()
    {
        IDomain d = new DefaultShapeDomain();
        Assert.Empty(d.DeclaredNeurons);
    }

    [Fact]
    public void Domain_can_declare_neurons()
    {
        IDomain d = new DomainWithNeurons();

        var only = Assert.Single(d.DeclaredNeurons);
        Assert.Equal(NeuronId.From("fake.do-thing"), only.Id);
    }

    private sealed class DomainWithPerGrain : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.Subset");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities =>
            [ new Capability.Llm(LlmTier.Reasoning) ];

        public IReadOnlyDictionary<Type, IReadOnlyList<Capability>> PerGrainCapabilities =>
            new Dictionary<Type, IReadOnlyList<Capability>>
            {
                [typeof(DomainWithPerGrain)] = [ new Capability.Llm(LlmTier.Reasoning) ],
            };
    }

    private sealed class DomainWithNeurons : IDomain
    {
        public DomainId Id => DomainId.From("Ino.Testing.WithVerbs");
        public string Version => "1.0.0";
        public IReadOnlyList<Capability> DeclaredCapabilities => [];
        public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
        [
            new NeuronDefinition(
                NeuronId.From("fake.do-thing"),
                DisplayName: "Do a thing",
                Description: "A test verb.",
                CanonicalSynapseType: typeof(object),
                PromptExamples: ["please do the thing"]),
        ];
    }
}
