using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Protocol.Domain.ValueObjects.Distribution;

namespace DigitalBrain.Os.Application;

public interface IMarketplace : INeuron, IHandle<PublishToMarketplace>, IHandle<InstallFromMarketplace>, IHandle<RunDistributionSimulation>, IHandle<ListPublished>
{
    Task<IReadOnlyList<ExperienceListing>> ListAsync(CancellationToken cancellationToken = default);

    Task<byte[]?> GetPackageBytesAsync(string experienceId, CancellationToken cancellationToken = default);

    Task<ExperienceListing> AddListingAsync(ExperienceManifest manifest, byte[] packageBytes, CancellationToken cancellationToken = default);

    Task<ExperienceListed> PublishLocalAsync(string experienceId, string? packagePath = null, CancellationToken cancellationToken = default);

    Task<ExperienceDownloaded> InstallListedAsync(string experienceId, CancellationToken cancellationToken = default);

    Task<ExperienceDownloaded> InstallFromPeerAsync(string peerAddress, string experienceId, CancellationToken cancellationToken = default);

    Task SyncListingsToGlobalAsync(string experienceId, CancellationToken cancellationToken = default);

    Task PullPopularFromGlobalAsync(CancellationToken cancellationToken = default);

    Task<ExperienceRated> RateExperienceAsync(string experienceId, int rating, string? comment = null, CancellationToken cancellationToken = default);
}
