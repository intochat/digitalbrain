using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Reshapes;
using Brain.Core.Endpoints;
using Brain.Core.Modules;
using Brain.Core.Outbox;
using Brain.Core.Reshapes;
using Xunit;

namespace Brain.Core.Tests;

public sealed class ReshapeRegistryTests
{
    [Fact]
    public void Registered_reshape_transforms_a_typed_event_once()
    {
        var fixture = new Fixture(EventVisibility.Published, EventVisibility.Published);
        var reshape = new PrefixReshape();
        fixture.Registry.Register(fixture.ReshapeId, fixture.Descriptor, reshape);

        var transformed = fixture.Registry.Transform(fixture.Snapshot(), new Produced("source"));

        var assessed = Assert.IsType<Assessed>(transformed);
        Assert.Equal("reshaped:source", assessed.Value);
        Assert.Equal(1, reshape.TransformCount);
    }

    [Fact]
    public void Unregistered_reshape_is_rejected_before_receiver_application()
    {
        var fixture = new Fixture(EventVisibility.Published, EventVisibility.Published);

        Assert.Throws<InvalidOperationException>(() => fixture.Registry.Validate(fixture.Snapshot(), new Produced("source")));
    }

    [Fact]
    public void Mismatched_snapshot_contracts_are_rejected()
    {
        var fixture = new Fixture(EventVisibility.Published, EventVisibility.Published);
        fixture.Registry.Register(fixture.ReshapeId, fixture.Descriptor, new PrefixReshape());
        var mismatched = fixture.Snapshot(input: fixture.Assessed, output: fixture.Assessed);

        Assert.Throws<InvalidOperationException>(() => fixture.Registry.Validate(mismatched, new Produced("source")));
    }

    [Fact]
    public void Cross_module_reshape_requires_published_input_and_output()
    {
        var fixture = new Fixture(EventVisibility.Internal, EventVisibility.Internal, targetConsumes: false);
        fixture.Registry.Register(fixture.ReshapeId, fixture.Descriptor, new PrefixReshape());

        Assert.Throws<InvalidOperationException>(() => fixture.Registry.Validate(fixture.Snapshot(targetModule: new ModuleId("summary")), new Produced("source")));
    }

    [Fact]
    public void Registry_rejects_a_registration_not_declared_by_its_owner_manifest()
    {
        var fixture = new Fixture(EventVisibility.Published, EventVisibility.Published);
        var undeclared = fixture.Descriptor with { OutputEvent = new ContractId("proof/other@1") };

        Assert.Throws<InvalidOperationException>(() => fixture.Registry.Register(fixture.ReshapeId, undeclared, new PrefixReshape()));
    }

    private sealed class Fixture
    {
        public Fixture(EventVisibility inputVisibility, EventVisibility outputVisibility, bool targetConsumes = true)
        {
            Produced = new ContractId("proof/produced@1");
            Assessed = new ContractId("proof/assessed@1");
            Owner = new ModuleId("proof");
            Descriptor = new ReshapeDescriptor(Produced, Assessed, Owner);
            ReshapeId = new ReshapeId(Guid.NewGuid());
            Modules = ManifestValidator.Validate(
            [
                new ModuleManifest(Owner, new ModuleVersion(1, 0, 0), [], [], [],
                    [new EventDescriptor(Produced, Owner, typeof(Produced), inputVisibility), new EventDescriptor(Assessed, Owner, typeof(Assessed), outputVisibility)],
                    [Produced], [Descriptor], [], []),
                new ModuleManifest(new ModuleId("summary"), new ModuleVersion(1, 0, 0), [], [], [], [], targetConsumes ? [Assessed] : [], [], [], []),
            ]);
            Registry = new ReshapeRegistry(Modules);
        }

        public ContractId Produced { get; }
        public ContractId Assessed { get; }
        public ModuleId Owner { get; }
        public ReshapeDescriptor Descriptor { get; }
        public ReshapeId ReshapeId { get; }
        public ModuleSet Modules { get; }
        public ReshapeRegistry Registry { get; }

        public DeliverySnapshot Snapshot(ContractId? input = null, ContractId? output = null, ModuleId? targetModule = null)
            => new(
                DeliveryId.New(),
                new EndpointAddress(new WorkspaceId("workspace/one"), targetModule ?? Owner, new NeuronRoleId("proof.target"), "workspace"),
                SynapseKey.New(),
                1,
                input ?? Produced,
                output ?? Assessed,
                ReshapeId);
    }

    private sealed class PrefixReshape : IReshape<Produced, Assessed>
    {
        public int TransformCount { get; private set; }

        public Assessed Transform(Produced source)
        {
            TransformCount++;
            return new Assessed($"reshaped:{source.Value}");
        }
    }

    private sealed record Produced(string Value) : IDomainEvent;
    private sealed record Assessed(string Value) : IDomainEvent;
}
