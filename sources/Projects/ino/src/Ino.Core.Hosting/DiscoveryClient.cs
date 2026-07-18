using System.Collections.Concurrent;
using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

public sealed class DiscoveryClient(IGrainFactory grains) : IDiscoveryClient
{
    private readonly ConcurrentDictionary<Type, CanonicalTarget> _canonicalCache = new();
    private readonly ConcurrentDictionary<Type, IReadOnlyList<ReactiveTarget>> _reactiveCache = new();

    public async Task<CanonicalTarget?> LookupCanonicalAsync(Type synapseType, CancellationToken ct = default)
    {
        if (_canonicalCache.TryGetValue(synapseType, out var cached)) return cached;
        var fresh = await grains.GetDiscovery().LookupCanonicalAsync(synapseType, ct);
        if (fresh is not null) _canonicalCache[synapseType] = fresh;
        return fresh;
    }

    public async Task<IReadOnlyList<ReactiveTarget>> LookupReactiveAsync(Type synapseType, CancellationToken ct = default)
    {
        if (_reactiveCache.TryGetValue(synapseType, out var cached)) return cached;
        var fresh = await grains.GetDiscovery().LookupReactiveAsync(synapseType, ct);
        if (fresh.Count > 0) _reactiveCache[synapseType] = fresh;
        return fresh;
    }

    public Task<IReadOnlyList<INeuronDefinition>> DumpNeuronsAsync(CancellationToken ct = default)
        => grains.GetDiscovery().DumpNeuronsAsync(ct);

    public void Invalidate()
    {
        _canonicalCache.Clear();
        _reactiveCache.Clear();
    }
}
