using DigitalBrain.Runtime.Marketplace;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Runtime.Neurons;

/// <summary>
/// Interface for the PostgresDbNeuron which coordinates database state for the marketplace ecosystem.
/// </summary>
public interface IPostgresDbNeuron : INeuronWithStringKey
{
    Task InsertBundleAsync(BundleInfo bundle);
    Task<List<BundleInfo>> SelectBundlesAsync();
    Task InsertPurchaseAsync(PurchaseRow purchase);
    Task<List<PurchaseRow>> SelectPurchasesAsync(string userId, string bundleId);
    Task InsertLicenseAsync(LicenseRow license);
    Task<List<LicenseRow>> SelectLicensesAsync(string userId, string bundleId);
}
