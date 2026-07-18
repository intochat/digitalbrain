using DigitalBrain.Kernel.Runtime.Neurons;
using DigitalBrain.Runtime.Marketplace;
using FluentAssertions;
using Xunit;

namespace DigitalBrain.InoLang.Tests;

// MKT-2: a published bundle's content is persisted and a buyer can download it,
// gated by entitlement for premium bundles.
public class MarketplaceDownloadTests
{
    [Fact]
    public async Task Download_ReturnsPersistedZip_ForFreeBundle()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var zip = new byte[] { 1, 2, 3, 4, 5 };
            var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
            await db.InsertBundleAsync(
                new BundleInfo("test/free-bundle", "1.0.0", "{}", System.Array.Empty<byte>(), "free", "mit", zip));

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");
            var bytes = await market.DownloadBundleAsync("test/free-bundle", "user-1");

            bytes.Should().Equal(zip);
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task Download_DeniesPremiumBundle_WithoutLicense()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            var zip = new byte[] { 9, 9, 9 };
            var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
            await db.InsertBundleAsync(
                new BundleInfo("test/premium-bundle", "1.0.0", "{}", System.Array.Empty<byte>(), "19.99", "commercial", zip));

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");
            var bytes = await market.DownloadBundleAsync("test/premium-bundle", "user-without-license");

            bytes.Should().BeEmpty("a premium bundle must not be downloadable without a license");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }
}
