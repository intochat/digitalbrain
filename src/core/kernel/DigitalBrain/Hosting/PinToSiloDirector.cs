using Orleans.Placement;
using Orleans.Runtime.MembershipService.SiloMetadata;
using Orleans.Runtime.Placement;

namespace DigitalBrain.Kernel;

internal sealed class PinToSiloDirector(ISiloMetadataCache metadata) : IPlacementFilterDirector
{
    internal const string SiloLabelKey = "db.silo";

    public IEnumerable<SiloAddress> Filter(PlacementFilterStrategy strategy, PlacementTarget target, IEnumerable<SiloAddress> silos)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var required = ((PinToSiloStrategy)strategy).Label;

        return silos.Where(silo =>
            metadata.GetSiloMetadata(silo).Metadata.TryGetValue(SiloLabelKey, out var label)
            && string.Equals(label, required, StringComparison.Ordinal));
    }
}
