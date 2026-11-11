using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Capabilities.Storage;

/// <summary>Resolves storage connections through the runtime CapabilityBroker.</summary>
public sealed class BrokerStorageOwners(CapabilityBroker broker) : IStorageOwnerSource
{
    private readonly CapabilityBroker _broker = broker ?? throw new ArgumentNullException(nameof(broker));

    /// <inheritdoc />
    public StorageOwner For(BrainId brain, string origin)
    {
        var scopedBroker = _broker.ForApp(origin);
        return async (item, ct) =>
        {
            var candidates = await scopedBroker.ResolveFor(brain, item, ct).ConfigureAwait(false);
            switch (candidates.Count)
            {
                case 0: throw new Unavailable(item);
                case 1: return candidates[0];
            }

            CapabilityConnection<IStorage>? owner = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    _ = await candidate.Capability.Stat(item, ct).ConfigureAwait(false);
                    if (owner is not null) { throw new Conflict(item); }
                    owner = candidate;
                }
                catch (StorageException error) when (error is NotFound or Denied or Unsupported)
                {
                }
            }

            return owner ?? throw new NotFound(item);
        };
    }
}
