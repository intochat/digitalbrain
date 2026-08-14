using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Core.Modules;
using Xunit;

namespace Brain.Core.Tests;

public sealed class ManifestValidatorTests
{
    [Fact]
    public void ValidatorRejectsDuplicateModuleIds()
    {
        var first = Module("proof");
        var second = Module("proof");

        AssertValidation(() => ManifestValidator.Validate([first, second]), "module");
    }

    [Fact]
    public void ValidatorRejectsDuplicateRoleIdsOwnedByDifferentModules()
    {
        var first = Module("proof-a", roles: [Role("proof.entry", "proof-a")]);
        var second = Module("proof-b", roles: [Role("proof.entry", "proof-b")]);

        AssertValidation(() => ManifestValidator.Validate([first, second]), "role");
    }

    [Fact]
    public void ValidatorRejectsMissingDependencyManifest()
    {
        var module = Module("proof", dependencies: [Dependency("missing", 1, 2)]);

        AssertValidation(() => ManifestValidator.Validate([module]), "dependency");
    }

    [Fact]
    public void ValidatorRejectsIncompatibleDependencyMajorVersion()
    {
        var provider = Module("provider", version: new ModuleVersion(2, 0, 0));
        var consumer = Module("consumer", dependencies: [Dependency("provider", 1, 2)]);

        AssertValidation(() => ManifestValidator.Validate([provider, consumer]), "compatible");
    }

    [Fact]
    public void ValidatorRejectsAConsumerOfAnInternalEventFromAnotherModule()
    {
        var producer = Module("producer", events: [Event("producer/finished@1", "producer", EventVisibility.Internal)]);
        var consumer = Module("consumer", consumedEvents: [new ContractId("producer/finished@1")]);

        AssertValidation(() => ManifestValidator.Validate([producer, consumer]), "internal event");
    }

    [Fact]
    public void ValidatorAllowsAnOwnerToConsumeItsInternalEvent()
    {
        var owner = Module(
            "proof",
            events: [Event("proof/finished@1", "proof", EventVisibility.Internal)],
            consumedEvents: [new ContractId("proof/finished@1")]);

        ManifestValidator.Validate([owner]);
    }

    [Fact]
    public void ValidatorRejectsAReshapeWithAnUndeclaredEvent()
    {
        var module = Module(
            "proof",
            events: [Event("proof/input@1", "proof", EventVisibility.Internal)],
            reshapes: [new ReshapeDescriptor(new ContractId("proof/input@1"), new ContractId("proof/output@1"), new ModuleId("proof"))]);

        AssertValidation(() => ManifestValidator.Validate([module]), "reshape");
    }

    [Fact]
    public void ValidatorRejectsDuplicateProvidedCapabilities()
    {
        var first = Module("first", providedCapabilities: [Capability("proof.work", "first")]);
        var second = Module("second", providedCapabilities: [Capability("proof.work", "second")]);

        AssertValidation(() => ManifestValidator.Validate([first, second]), "capability");
    }

    [Fact]
    public void ValidatorRejectsAnOperationWhoseEntryRoleIsNotOwnedByItsManifest()
    {
        var module = Module("proof", operations: [Operation("proof.run", "proof.entry", "proof")]);

        AssertValidation(() => ManifestValidator.Validate([module]), "entry role");
    }

    [Fact]
    public void RegistryResolvesAndExposesDeclaredDescriptors()
    {
        var role = Role("proof.entry", "proof");
        var operation = Operation("proof.run", "proof.entry", "proof");
        var @event = Event("proof/finished@1", "proof", EventVisibility.Published);
        var capability = Capability("proof.work", "proof");
        var manifest = Module(
            "proof",
            roles: [role],
            operations: [operation],
            events: [@event],
            providedCapabilities: [capability]);
        IModuleRegistry registry = new ModuleRegistry();

        var resolved = registry.Resolve([manifest]);

        Assert.Single(resolved.Modules);
        Assert.Same(manifest, registry.Get(new ModuleId("proof")));
        Assert.Same(operation, registry.GetOperation(new OperationId("proof.run")));
        Assert.Same(@event, registry.GetEvent(new ContractId("proof/finished@1")));
        Assert.Same(capability, registry.GetCapability(new CapabilityId("proof.work")));
        Assert.Throws<KeyNotFoundException>(() => registry.Get(new ModuleId("missing")));
    }

    private static ModuleManifest Module(
        string id,
        ModuleVersion? version = null,
        IReadOnlyCollection<ModuleDependency>? dependencies = null,
        IReadOnlyCollection<RoleDescriptor>? roles = null,
        IReadOnlyCollection<OperationDescriptor>? operations = null,
        IReadOnlyCollection<EventDescriptor>? events = null,
        IReadOnlyCollection<ContractId>? consumedEvents = null,
        IReadOnlyCollection<ReshapeDescriptor>? reshapes = null,
        IReadOnlyCollection<CapabilityDescriptor>? providedCapabilities = null,
        IReadOnlyCollection<CapabilityId>? requiredCapabilities = null)
        => new(
            new ModuleId(id),
            version ?? new ModuleVersion(1, 0, 0),
            dependencies ?? [],
            roles ?? [],
            operations ?? [],
            events ?? [],
            consumedEvents ?? [],
            reshapes ?? [],
            providedCapabilities ?? [],
            requiredCapabilities ?? []);

    private static ModuleDependency Dependency(string moduleId, int minimumMajor, int maximumExclusiveMajor)
        => new(new ModuleId(moduleId), new ModuleVersion(minimumMajor, 0, 0), new ModuleVersion(maximumExclusiveMajor, 0, 0));

    private static RoleDescriptor Role(string id, string owner)
        => new(new NeuronRoleId(id), new ModuleId(owner));

    private static OperationDescriptor Operation(string id, string entryRole, string owner)
        => new(
            new OperationId(id),
            new ContractId("proof/run-input@1"),
            new ContractId("proof/run-result@1"),
            new NeuronRoleId(entryRole),
            new ModuleId(owner),
            new ContractVersion(1));

    private static EventDescriptor Event(string contract, string owner, EventVisibility visibility)
        => new(new ContractId(contract), new ModuleId(owner), typeof(ProofFinished), visibility);

    private static CapabilityDescriptor Capability(string id, string owner)
        => new(
            new CapabilityId(id),
            new ContractId("proof/work-request@1"),
            new ContractId("proof/work-result@1"),
            new ModuleId(owner),
            new ContractVersion(1));

    private static void AssertValidation(Action action, string expectedMessage)
    {
        var error = Assert.Throws<ManifestValidationException>(action);
        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProofFinished : IDomainEvent;
}
