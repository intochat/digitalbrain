using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Placement;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Kernel;

// Cluster-singleton pinned to the system silo via PinToSilo. Default Orleans
// placement would activate Discovery on whichever silo the first caller
// happens to be on, producing one activation per silo under parallel startup
// — writers on identity/domains would register entries into their own
// local activations and readers on system would miss them. PinToSilo narrows
// candidate silos to those tagged with UseSiloMetadata["ino.silo"] == "kernel"
// before placement runs, so all calls converge on the same activation.
[PinToSilo("kernel")]
public sealed class Discovery(ILogger<Discovery> logger) : Grain, IDiscovery
{
    private readonly Dictionary<Type, CanonicalRecord> _canonical = new();
    private readonly Dictionary<Type, List<ReactiveRecord>> _reactive = new();
    private readonly Dictionary<DomainId, int> _countsBySilo = new();
    private readonly Dictionary<DomainId, IReadOnlyList<INeuronDefinition>> _neuronsBySilo = new();
    // Dynamic neurons created at runtime by the L1 loop. Merged into the
    // per-silo static set on every DumpNeuronsAsync so Cortex picks them
    // up on the next routing pass without a silo restart. Keyed by
    // NeuronId so a re-registration with the same id replaces the prior
    // entry (e.g. CreatorNeuron rebuilds the script body).
    private readonly Dictionary<NeuronId, INeuronDefinition> _dynamicNeurons = new();

    public Task RegisterAsync(SiloRegistration registration, CancellationToken ct = default)
    {
        ClearEntriesForSilo(registration.Silo);

        _neuronsBySilo[registration.Silo] = registration.Neurons;

        foreach (var canonical in registration.Canonical)
        {
            if (_canonical.TryGetValue(canonical.SynapseType, out var existing))
            {
                throw DiscoveryConflictException.Canonical(
                    canonical.SynapseType,
                    existing.GrainType, existing.Silo,
                    canonical.GrainType, registration.Silo);
            }
            _canonical[canonical.SynapseType] = new CanonicalRecord(
                canonical.GrainType, canonical.Domain, canonical.RequiredCapabilities,
                registration.Silo);
        }

        foreach (var reactive in registration.Reactive)
        {
            if (!_reactive.TryGetValue(reactive.SynapseType, out var list))
                _reactive[reactive.SynapseType] = list = new List<ReactiveRecord>();
            list.Add(new ReactiveRecord(reactive.GrainType, reactive.Domain, registration.Silo));
        }

        _countsBySilo[registration.Silo] = registration.Canonical.Count + registration.Reactive.Count;
        logger.LogInformation("Discovery registered {Canonical} canonical + {Reactive} reactive targets for silo {Silo}",
            registration.Canonical.Count, registration.Reactive.Count, registration.Silo);
        return Task.CompletedTask;
    }

    public Task<CanonicalTarget?> LookupCanonicalAsync(Type synapseType, CancellationToken ct = default)
    {
        if (_canonical.TryGetValue(synapseType, out var rec))
            return Task.FromResult<CanonicalTarget?>(new CanonicalTarget(
                synapseType, rec.GrainType, rec.Domain, rec.RequiredCapabilities));
        return Task.FromResult<CanonicalTarget?>(null);
    }

    public Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct = default)
    {
        if (_reactive.TryGetValue(synapseType, out var list))
            return Task.FromResult<IReadOnlyList<ReactiveTarget>>(
                list.Select(r => new ReactiveTarget(synapseType, r.GrainType, r.Domain)).ToArray());
        return Task.FromResult<IReadOnlyList<ReactiveTarget>>(Array.Empty<ReactiveTarget>());
    }

    public Task<DiscoveryDump> DumpAsync(CancellationToken ct = default)
    {
        var canonicals = _canonical
            .Select(kv => new CanonicalTarget(kv.Key, kv.Value.GrainType, kv.Value.Domain, kv.Value.RequiredCapabilities))
            .ToArray();
        var reactives = _reactive
            .SelectMany(kv => kv.Value.Select(r => new ReactiveTarget(kv.Key, r.GrainType, r.Domain)))
            .ToArray();
        var counts = _countsBySilo.ToDictionary(kv => kv.Key.Value, kv => kv.Value);

        return Task.FromResult(new DiscoveryDump(canonicals, reactives, counts));
    }

    public Task<IReadOnlyList<INeuronDefinition>> DumpNeuronsAsync(CancellationToken ct = default)
    {
        var aggregate = _neuronsBySilo.Values
            .SelectMany(v => v)
            .Concat(_dynamicNeurons.Values)
            .ToArray();
        return Task.FromResult<IReadOnlyList<INeuronDefinition>>(aggregate);
    }

    public Task RegisterDynamicNeuronAsync(INeuronDefinition neuron, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(neuron);
        _dynamicNeurons[neuron.Id] = neuron;
        logger.LogInformation(
            "Discovery registered dynamic neuron {Id} (planType={PlanType}, total dynamics: {Count})",
            neuron.Id, neuron.PlanType?.FullName ?? "<none>", _dynamicNeurons.Count);
        return Task.CompletedTask;
    }

    private void ClearEntriesForSilo(DomainId silo)
    {
        var staleCanonical = _canonical
            .Where(kv => kv.Value.Silo == silo)
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var key in staleCanonical)
            _canonical.Remove(key);

        var emptyReactive = new List<Type>();
        foreach (var (key, list) in _reactive)
        {
            list.RemoveAll(r => r.Silo == silo);
            if (list.Count == 0) emptyReactive.Add(key);
        }
        foreach (var key in emptyReactive)
            _reactive.Remove(key);

        _countsBySilo.Remove(silo);
        _neuronsBySilo.Remove(silo);
    }

    private sealed record CanonicalRecord(Type GrainType, DomainId Domain, IReadOnlyList<Capability> RequiredCapabilities, DomainId Silo);
    private sealed record ReactiveRecord(Type GrainType, DomainId Domain, DomainId Silo);
}
