namespace DigitalBrain.Core;

[GenerateSerializer]
public record FilterMarketplace(
    [property: Id(0)] string? Tier = null,
    [property: Id(1)] string? Channel = null
) : Synapse(nameof(FilterMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record ListPublished() : Synapse(nameof(ListPublished), DateTimeOffset.UtcNow);

public interface IMarketplaceNeuron : INeuron, IHandle<PublishToMarketplace>, IHandle<InstallFromMarketplace>, IHandle<ListPublished>, IHandle<FilterMarketplace>;

// Remote client contract for the private marketplace service (new repo).
// Kernel's MarketplaceNeuron becomes a thin proxy when RemoteMarketplaceBaseUrl is configured.
// This keeps local stub mode for security/air-gapped while enabling cloud pay-go distribution.
public interface IRemoteMarketplaceClient
{
    Task PublishAsync(PublishToMarketplace cmd);
    Task InstallAsync(InstallFromMarketplace cmd);
    Task<PublishedList> ListAsync();
    // Security policy, user entitlement queries etc. added as the private service is built.
}

// Richer publish/install commands that carry full pack data for real marketplace behavior.
// Old simple constructors still work via defaults for minimal compat during transition.
[GenerateSerializer]
public record PublishToMarketplace(
    string PackName,
    string Version,
    string Code = "",
    string OwnerId = "anonymous",
    bool IsPrivate = false,
    double CommissionRate = 0.10,
    string Description = "",
    string AuthorPublicKeyBase64 = "",
    string SignatureBase64 = "",
    decimal Price = 0m
) : Synapse(nameof(PublishToMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record InstallFromMarketplace(
    string PackName,
    string Version,
    string BuyerId = "anonymous",
    string? SessionId = null
) : Synapse(nameof(InstallFromMarketplace), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record NeuroPackInstalled(NeuroPack Pack) : Synapse(nameof(NeuroPackInstalled), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record MarketplaceInstallStaged(
    string ProposalId,
    string MarketplaceNeuronId,
    NeuroPack Pack,
    string BuyerId,
    string? SessionId,
    double CommissionAmount
) : Synapse(nameof(MarketplaceInstallStaged), DateTimeOffset.UtcNow);

[GenerateSerializer]
public record PublishedList(IReadOnlyList<NeuroPack> Packs) : Synapse(nameof(PublishedList), DateTimeOffset.UtcNow);

// Commission event - fired on successful install to support marketplace economics.
[GenerateSerializer]
public record CommissionTaken(
    string PackName,
    string Version,
    string BuyerId,
    string SellerId,
    double CommissionRate,
    double CommissionAmount
) : Synapse(nameof(CommissionTaken), default);

