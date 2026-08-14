using System.Collections.ObjectModel;
using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Events;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;

namespace Brain.Core.Modules;

public sealed class ModuleSet
{
    internal ModuleSet(
        IReadOnlyCollection<ModuleManifest> modules,
        IReadOnlyDictionary<string, ModuleManifest> moduleIndex,
        IReadOnlyDictionary<string, OperationDescriptor> operationIndex,
        IReadOnlyDictionary<string, EventDescriptor> eventIndex,
        IReadOnlyDictionary<string, CapabilityDescriptor> capabilityIndex)
    {
        Modules = Array.AsReadOnly(modules.ToArray());
        ModuleIndex = ReadOnly(moduleIndex);
        OperationIndex = ReadOnly(operationIndex);
        EventIndex = ReadOnly(eventIndex);
        CapabilityIndex = ReadOnly(capabilityIndex);
    }

    public IReadOnlyList<ModuleManifest> Modules { get; }

    internal IReadOnlyDictionary<string, ModuleManifest> ModuleIndex { get; }

    internal IReadOnlyDictionary<string, OperationDescriptor> OperationIndex { get; }

    internal IReadOnlyDictionary<string, EventDescriptor> EventIndex { get; }

    internal IReadOnlyDictionary<string, CapabilityDescriptor> CapabilityIndex { get; }

    private static IReadOnlyDictionary<string, T> ReadOnly<T>(IReadOnlyDictionary<string, T> source)
        => new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(source, StringComparer.Ordinal));
}

public interface IModuleRegistry
{
    ModuleSet Resolve(IReadOnlyCollection<ModuleManifest> installed);

    ModuleManifest Get(ModuleId id);

    OperationDescriptor GetOperation(OperationId id);

    EventDescriptor GetEvent(ContractId id);

    CapabilityDescriptor GetCapability(CapabilityId id);
}

public sealed class ModuleRegistry : IModuleRegistry
{
    private ModuleSet? _resolved;

    public ModuleSet Resolve(IReadOnlyCollection<ModuleManifest> installed)
        => _resolved = ManifestValidator.Validate(installed);

    public ModuleManifest Get(ModuleId id)
        => Current.ModuleIndex.TryGetValue(id.Value, out var manifest)
            ? manifest
            : throw Missing("module", id.Value);

    public OperationDescriptor GetOperation(OperationId id)
        => Current.OperationIndex.TryGetValue(id.Value, out var operation)
            ? operation
            : throw Missing("operation", id.Value);

    public EventDescriptor GetEvent(ContractId id)
        => Current.EventIndex.TryGetValue(id.Value, out var @event)
            ? @event
            : throw Missing("event", id.Value);

    public CapabilityDescriptor GetCapability(CapabilityId id)
        => Current.CapabilityIndex.TryGetValue(id.Value, out var capability)
            ? capability
            : throw Missing("capability", id.Value);

    private ModuleSet Current => _resolved ?? throw new InvalidOperationException(
        "No module set has been resolved. Call Resolve before reading module declarations.");

    private static KeyNotFoundException Missing(string kind, string id)
        => new($"Resolved module set does not contain {kind} '{id}'.");
}
