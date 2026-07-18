using Ino.Core;

namespace Ino.Aspire.Hosting;

public sealed record MarketplaceFeed(IReadOnlyList<MarketplaceFeedEntry> Domains);

public sealed record MarketplaceFeedEntry(
    DomainId Id,
    string Description,
    string Version,
    IReadOnlyList<MarketplaceNeuronMetadata> Neurons);

public sealed record MarketplaceNeuronMetadata(
    NeuronId Id,
    string DisplayName,
    string Description);
