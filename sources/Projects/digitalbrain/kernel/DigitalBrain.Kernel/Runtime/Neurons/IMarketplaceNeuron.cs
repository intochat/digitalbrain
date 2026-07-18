using DigitalBrain.Runtime.Marketplace;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Runtime.Neurons;

/// <summary>
/// Interface for the MarketplaceNeuron which orchestrates discovery, publishing, and purchasing of experience bundles.
/// </summary>
public interface IMarketplaceNeuron : INeuronWithStringKey
{
    Task<List<BundleInfo>> GetCatalogAsync();
    Task<PublishBundleResponse> PublishBundleAsync(string bundleId, string version, string manifestJson, byte[] zipBytes);
    Task<BuyBundleResponse> BuyBundleAsync(string bundleId, string userId);
    Task<ConfirmCheckoutResponse> ConfirmCheckoutAsync(string stripeEventJson, string stripeSignature);
    Task<byte[]> DownloadBundleAsync(string bundleId, string userId);
    Task<InstallMarketplaceNeuronResponse> InstallMarketplaceNeuronAsync(string bundleId, string userId);
    Task<byte[]> GetPublicKeyAsync();
}
