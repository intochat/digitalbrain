using DigitalBrain.InoLang.Linking;
using Xunit;
using FluentAssertions;

namespace DigitalBrain.InoLang.Tests.Parsing;

public sealed class LayeredContractCatalogTests
{
    private sealed class FakeCatalog : IContractCatalog
    {
        private readonly Dictionary<string, ContractSchema> _schemas = new(StringComparer.Ordinal);

        public ContractSchema? Resolve(string fqn) => _schemas.GetValueOrDefault(fqn);

        public IReadOnlyCollection<ContractSchema> GetAllSchemas() => _schemas.Values;

        public void Register(ContractSchema schema) => _schemas[schema.Fqn] = schema;

        public FakeCatalog With(string fqn, ContractKind kind, params string[] fields)
        {
            _schemas[fqn] = new ContractSchema(fqn, kind, fields);
            return this;
        }
    }

    [Fact]
    public void Primary_wins_when_both_define_the_same_fqn()
    {
        var primary = new FakeCatalog()
            .With("Foo.Bar", ContractKind.Synapse, "a");
        var fallback = new FakeCatalog()
            .With("Foo.Bar", ContractKind.Synapse, "z");

        var layered = new LayeredContractCatalog(primary, fallback);

        var schema = layered.Resolve("Foo.Bar");

        schema.Should().NotBeNull();
        schema!.Kind.Should().Be(ContractKind.Synapse);
        schema.Fields.Should().Equal("a");
    }

    [Fact]
    public void Fallback_resolves_what_primary_does_not()
    {
        var primary = new FakeCatalog();
        var fallback = new FakeCatalog()
            .With("Acme.Thing", ContractKind.Neuron);

        var layered = new LayeredContractCatalog(primary, fallback);

        layered.Resolve("Acme.Thing").Should().NotBeNull();
    }

    [Fact]
    public void Resolve_returns_null_when_neither_catalog_has_the_fqn()
    {
        var layered = new LayeredContractCatalog(new FakeCatalog(), new FakeCatalog());

        layered.Resolve("Nope.NotReal").Should().BeNull();
    }

    [Fact]
    public void Layering_preserves_kind_and_fields_from_the_winning_catalog()
    {
        var primary = new FakeCatalog()
            .With("DigitalBrain.DomainInstalled", ContractKind.Synapse, "domain", "profile");
        var fallback = new FakeCatalog()
            .With("Other.Thing", ContractKind.Neuron);

        var layered = new LayeredContractCatalog(primary, fallback);

        var bootFloor = layered.Resolve("DigitalBrain.DomainInstalled");
        bootFloor.Should().NotBeNull();
        bootFloor!.Kind.Should().Be(ContractKind.Synapse);
        bootFloor.Fields.Should().BeEquivalentTo(["domain", "profile"]);

        var fallbackHit = layered.Resolve("Other.Thing");
        fallbackHit.Should().NotBeNull();
        fallbackHit!.Kind.Should().Be(ContractKind.Neuron);
    }

    [Fact]
    public void Constructor_rejects_null_arguments()
    {
        var some = new FakeCatalog();

        var firstNull = () => new LayeredContractCatalog(null!, some);
        var secondNull = () => new LayeredContractCatalog(some, null!);

        firstNull.Should().Throw<ArgumentNullException>();
        secondNull.Should().Throw<ArgumentNullException>();
    }
}
