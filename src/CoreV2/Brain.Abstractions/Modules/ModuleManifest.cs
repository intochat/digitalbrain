using System.Collections.ObjectModel;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;

namespace Brain.Abstractions.Modules;

public enum NeuronScope
{
    Workspace,
    Principal,
}

public record NeuronRoleDescriptor(NeuronRoleId Id, NeuronScope Scope, ModuleId Owner);

// Kept as a source-compatible shorthand for pre-scope manifests. New declarations should
// state their scope explicitly through NeuronRoleDescriptor.
public sealed record RoleDescriptor : NeuronRoleDescriptor
{
    public RoleDescriptor(NeuronRoleId id, ModuleId owner)
        : base(id, NeuronScope.Workspace, owner)
    {
    }

    public RoleDescriptor(NeuronRoleId id, NeuronScope scope, ModuleId owner)
        : base(id, scope, owner)
    {
    }
}

public sealed record ReshapeDescriptor(ContractId InputEvent, ContractId OutputEvent, ModuleId Owner);

public sealed class ModuleManifest
{
    public ModuleManifest(
        ModuleId id,
        ModuleVersion version,
        IReadOnlyCollection<ModuleDependency> dependencies,
        IReadOnlyCollection<NeuronRoleDescriptor> roles,
        IReadOnlyCollection<OperationDescriptor> operations,
        IReadOnlyCollection<EventDescriptor> events,
        IReadOnlyCollection<ContractId> consumedEvents,
        IReadOnlyCollection<ReshapeDescriptor> reshapes,
        IReadOnlyCollection<CapabilityDescriptor> providedCapabilities,
        IReadOnlyCollection<CapabilityId> requiredCapabilities)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
        {
            throw new ArgumentException("A module manifest requires a module id.", nameof(id));
        }

        Id = id;
        Version = version;
        Dependencies = Copy(dependencies, nameof(dependencies));
        Roles = Copy(roles, nameof(roles));
        Operations = Copy(operations, nameof(operations));
        Events = Copy(events, nameof(events));
        ConsumedEvents = Copy(consumedEvents, nameof(consumedEvents));
        Reshapes = Copy(reshapes, nameof(reshapes));
        ProvidedCapabilities = Copy(providedCapabilities, nameof(providedCapabilities));
        RequiredCapabilities = Copy(requiredCapabilities, nameof(requiredCapabilities));
    }

    public ModuleId Id { get; }

    public ModuleVersion Version { get; }

    public IReadOnlyList<ModuleDependency> Dependencies { get; }

    public IReadOnlyList<NeuronRoleDescriptor> Roles { get; }

    public IReadOnlyList<OperationDescriptor> Operations { get; }

    public IReadOnlyList<EventDescriptor> Events { get; }

    public IReadOnlyList<ContractId> ConsumedEvents { get; }

    public IReadOnlyList<ReshapeDescriptor> Reshapes { get; }

    public IReadOnlyList<CapabilityDescriptor> ProvidedCapabilities { get; }

    public IReadOnlyList<CapabilityId> RequiredCapabilities { get; }

    private static IReadOnlyList<T> Copy<T>(IReadOnlyCollection<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
