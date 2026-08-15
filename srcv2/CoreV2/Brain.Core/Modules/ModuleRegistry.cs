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

public sealed record ModuleRegistrySnapshot(long Generation, ModuleSet Modules);

public interface IModuleRegistrySnapshotLease : IDisposable
{
    ModuleRegistrySnapshot Snapshot { get; }
}

public interface IModuleRegistry
{
    ModuleSet Resolve(IReadOnlyCollection<ModuleManifest> installed);

    ModuleRegistrySnapshot GetSnapshot();

    IModuleRegistrySnapshotLease AcquireSnapshot();

    ModuleManifest Get(ModuleId id);

    OperationDescriptor GetOperation(OperationId id);

    EventDescriptor GetEvent(ContractId id);

    CapabilityDescriptor GetCapability(CapabilityId id);
}

public sealed class ModuleRegistry : IModuleRegistry
{
    private long _generation;
    private readonly ReaderWriterLockSlim _snapshotLock = new(LockRecursionPolicy.NoRecursion);
    private ModuleRegistrySnapshot? _snapshot;

    public ModuleSet Resolve(IReadOnlyCollection<ModuleManifest> installed)
    {
        var resolved = ManifestValidator.Validate(installed);
        _snapshotLock.EnterWriteLock();
        try
        {
            var snapshot = new ModuleRegistrySnapshot(++_generation, resolved);
            Volatile.Write(ref _snapshot, snapshot);
            return resolved;
        }
        finally
        {
            _snapshotLock.ExitWriteLock();
        }
    }

    public ModuleRegistrySnapshot GetSnapshot()
        => Volatile.Read(ref _snapshot) ?? throw new InvalidOperationException(
            "No module set has been resolved. Call Resolve before reading module declarations.");

    public IModuleRegistrySnapshotLease AcquireSnapshot()
    {
        _snapshotLock.EnterReadLock();
        try
        {
            return new SnapshotLease(this, GetSnapshot());
        }
        catch
        {
            _snapshotLock.ExitReadLock();
            throw;
        }
    }

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

    private ModuleSet Current => GetSnapshot().Modules;

    private static KeyNotFoundException Missing(string kind, string id)
        => new($"Resolved module set does not contain {kind} '{id}'.");

    private sealed class SnapshotLease(
        ModuleRegistry owner,
        ModuleRegistrySnapshot snapshot) : IModuleRegistrySnapshotLease
    {
        private ModuleRegistry? _owner = owner;

        public ModuleRegistrySnapshot Snapshot { get; } = snapshot;

        public void Dispose()
        {
            var currentOwner = Interlocked.Exchange(ref _owner, null);
            currentOwner?._snapshotLock.ExitReadLock();
        }
    }
}
