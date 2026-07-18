using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Location.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Journaling;

namespace Ino.Domains.Location.Neurons;

/// <summary>
/// Per-user location journal. Implements <see cref="ILocationNeuron"/> for
/// explicit user-keyed recording and inherits <see cref="Neuron{TEvent}"/>'s
/// <see cref="Ino.Core.Hosting.IJournaledNeuronQuery{TEvent}"/> surface so
/// any cross-silo plan can read its event log via
/// <see cref="ITraversalEngine.VisitAsync"/>.
///
/// Naturally placed by Orleans on the Location silo — it's the only silo whose
/// assembly registers <see cref="LocationNeuron"/>. Per-domain trace filtering
/// (<c>service.name=Ino.Domains.Location</c>) makes the journal queries easy
/// to isolate in the Aspire dashboard. <c>[PinToSilo]</c> is reserved for
/// cluster-singleton grains (Discovery, Cortex) — single-domain grains route
/// correctly without it.
/// </summary>
public sealed class LocationNeuron(
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<LocationVisited>> journal,
    ILogger<LocationNeuron>? logger = null)
    : Neuron<LocationVisited>(journal), ILocationNeuron
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public Task RecordAsync(string place, string? label, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(place);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId(correlationId),
            Source: new Caller.FromDomain(DomainId.From("Ino.Domains.Location")),
            SourceStream: new StreamKey($"location:{this.GetPrimaryKeyString()}"))
        {
            FirePort = new NoOpFirePort(),
            Logger = _logger,
        };

        return RaiseAsync(new LocationVisited(place, label), ctx);
    }
}
