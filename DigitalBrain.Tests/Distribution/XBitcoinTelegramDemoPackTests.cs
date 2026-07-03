using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Distribution;

public sealed class XBitcoinTelegramDemoPackTests : NeuronTestBase
{
    private const string PackName = "DigitalBrain.Experience.XBitcoinTelegramDemo";

    [Fact]
    public async Task Installed_pack_reacts_to_matching_XPostReceived_with_CheckBitcoinPrice()
    {
        var market = Grain<IMarketplaceNeuron>("market-demo-pack-smoke");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0.0", Code: MarketplaceSeeds.XBitcoinTelegramDemoPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0.0", BuyerId: "smoke-test-user"));

        var ingress = Grain<IIngressNeuron>("ingress-smoke");
        await ingress.IngestAsync("XPostReceived",
            new Dictionary<string, object?> { ["author"] = "elon", ["text"] = "big news", ["chatId"] = 7L });

        var gen = Grain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());
        Signal? checkPrice = null;
        for (var attempt = 0; attempt < 40 && checkPrice is null; attempt++)
        {
            await Task.Delay(50);
            var timeline = await gen.GetOutgoingTimelineAsync();
            checkPrice = timeline.OfType<Signal>().FirstOrDefault(s => s.Name == "CheckBitcoinPrice");
        }

        Assert.NotNull(checkPrice);
        Assert.Equal(7L, checkPrice!.Props["chatId"]);
        Assert.Equal("elon", checkPrice.Props["author"]);
    }

    [Fact]
    public async Task Installed_pack_ignores_XPostReceived_from_an_unwatched_author()
    {
        var market = Grain<IMarketplaceNeuron>("market-demo-pack-smoke-2");
        await market.FireAsync(new PublishToMarketplace(
            PackName, "1.0.0", Code: MarketplaceSeeds.XBitcoinTelegramDemoPackCode,
            OwnerId: "tester", IsPrivate: false, CommissionRate: 0.0));
        await market.FireAsync(new InstallFromMarketplace(PackName, "1.0.0", BuyerId: "smoke-test-user-2"));

        var ingress = Grain<IIngressNeuron>("ingress-smoke-2");
        await ingress.IngestAsync("XPostReceived",
            new Dictionary<string, object?> { ["author"] = "someone_else", ["text"] = "irrelevant", ["chatId"] = 9L });

        var gen = Grain<IGeneratedNeuron>("generated-" + PackName.ToLowerInvariant());
        await Task.Delay(300);
        var timeline = await gen.GetOutgoingTimelineAsync();
        Assert.DoesNotContain(timeline.OfType<Signal>(), s => s.Name == "CheckBitcoinPrice");
    }
}
