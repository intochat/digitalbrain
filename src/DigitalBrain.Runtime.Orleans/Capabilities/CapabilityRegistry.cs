using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Capabilities.Storage;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>How many connections of a brain may provide a capability.</summary>
public enum CapabilityConnectionMode
{
    /// <summary>Any number, resolved in Settings order (storage).</summary>
    ManyConnections,

    /// <summary>Exactly one, resolved from the host's keyed registration (secrets, identity).</summary>
    OneConnection,
}

/// <summary>
/// The capabilities the runtime knows: id to contract interface, connection mode and any extra binding interfaces a host accepts
/// as an implementation (R4-02, D-09). Seeded with storage@1 (many), secrets@1 and identity@1 (single), and the contract-less
/// circle@1 the local+cloud topology requires. Not thread-safe while being configured; hosts configure it before startup.
/// </summary>
public sealed class CapabilityRegistry
{
    private readonly Dictionary<CapabilityId, Entry> _entries = [];

    /// <summary>Creates the registry with the shipped capabilities.</summary>
    public CapabilityRegistry()
    {
        Register(Capability.Storage.V1, typeof(IStorage), CapabilityConnectionMode.ManyConnections);
        Register(Capability.Secrets.V1, typeof(ISecrets), CapabilityConnectionMode.OneConnection);
        Register(Capability.Identity.V1, typeof(IIdentity), CapabilityConnectionMode.OneConnection);
        Register(LocalCloudProfile.Circle, null, CapabilityConnectionMode.ManyConnections);
    }

    /// <summary>Every known capability.</summary>
    public IReadOnlyCollection<CapabilityId> Capabilities => _entries.Keys;

    /// <summary>Adds a capability; <paramref name="contract"/> null means modules need no neuron implementing it.</summary>
    /// <exception cref="InvalidOperationException">The capability is already registered.</exception>
    public void Register(CapabilityId capability, Type? contract, CapabilityConnectionMode mode)
    {
        if (!_entries.TryAdd(capability, new Entry(contract, mode)))
        {
            throw new InvalidOperationException($"Capability {capability} is already registered.");
        }
    }

    /// <summary>Lets a host accept <paramref name="binding"/> as an implementation of <paramref name="capability"/> (e.g.: IStorage).</summary>
    /// <exception cref="InvalidOperationException">The capability is unknown.</exception>
    public void AcceptBinding(CapabilityId capability, Type binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!_entries.TryGetValue(capability, out var entry))
        {
            throw new InvalidOperationException($"Cannot accept {binding.FullName} for unknown capability {capability}.");
        }
        if (!entry.Bindings.Contains(binding)) { entry.Bindings.Add(binding); }
    }

    /// <summary>True when <paramref name="capability"/> (name and major) is registered.</summary>
    public bool Known(CapabilityId capability) => _entries.ContainsKey(capability);

    /// <summary>The contract interface of <paramref name="capability"/>, or null when unknown or contract-less.</summary>
    public Type? ContractOf(CapabilityId capability) => _entries.TryGetValue(capability, out var entry) ? entry.Contract : null;

    /// <summary>True when <paramref name="capability"/> is a single-connection capability.</summary>
    public bool IsSingle(CapabilityId capability)
        => _entries.TryGetValue(capability, out var entry) && entry.Mode == CapabilityConnectionMode.OneConnection;

    /// <summary>The contract and every accepted binding of <paramref name="capability"/>.</summary>
    public IReadOnlyList<Type> BindingsOf(CapabilityId capability)
        => _entries.TryGetValue(capability, out var entry)
            ? [.. (entry.Contract is null ? [] : new[] { entry.Contract }), .. entry.Bindings]
            : [];

    /// <summary>
    /// The capability <paramref name="type"/> stands for: its <c>[Capability]</c> attribute when it is a contract (IGrants maps to
    /// identity@1), else the capability that accepted it as a binding.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type is neither a known contract nor an accepted binding.</exception>
    public CapabilityId CapabilityOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.GetCustomAttribute<CapabilityAttribute>(inherit: false) is { } attribute && Known(attribute.Id)) { return attribute.Id; }
        foreach (var (id, entry) in _entries)
        {
            if (entry.Contract == type || entry.Bindings.Contains(type)) { return id; }
        }
        throw new InvalidOperationException($"{type.FullName} is not a capability contract or an accepted binding of a known capability.");
    }

    private sealed record Entry(Type? Contract, CapabilityConnectionMode Mode)
    {
        public List<Type> Bindings { get; } = [];
    }
}
