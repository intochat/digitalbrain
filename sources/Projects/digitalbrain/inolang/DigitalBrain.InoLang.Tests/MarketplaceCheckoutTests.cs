using System.Text.Json;
using DigitalBrain.Kernel.Runtime.Neurons;
using DigitalBrain.Runtime.Marketplace;
using FluentAssertions;
using Xunit;

namespace DigitalBrain.InoLang.Tests;

// MKT-4: a buy opens a Stripe Checkout session and grants no entitlement; the license is
// minted only after a verified checkout.session.completed webhook. Payment before license.
public class MarketplaceCheckoutTests
{
    private static void SeedPremiumBundle(TestDigitalBrain brain, string bundleId, string price = "19.99")
    {
        var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
        db.InsertBundleAsync(
            new BundleInfo(bundleId, "1.0.0", "{}", System.Array.Empty<byte>(), price, "commercial", System.Array.Empty<byte>()))
            .GetAwaiter().GetResult();
    }

    // A faithful checkout.session.completed event whose session carries the marketplace
    // metadata, exactly as Stripe echoes back what BuyBundleAsync set on the session.
    private static string CompletedEvent(string bundleId, string userId, string sessionId)
        => StripeEvent("checkout.session.completed", bundleId, userId, sessionId, includeSession: true);

    private static string ExpiredEvent(string bundleId, string userId, string sessionId)
        => StripeEvent("checkout.session.expired", bundleId, userId, sessionId, includeSession: true);

    private static string StripeEvent(string type, string bundleId, string userId, string sessionId, bool includeSession)
    {
        object sessionObject = new
        {
            id = sessionId,
            @object = "checkout.session",
            client_reference_id = userId,
            metadata = new Dictionary<string, string> { ["BundleId"] = bundleId, ["UserId"] = userId },
        };

        var evt = new
        {
            id = "evt_" + Guid.NewGuid().ToString("N"),
            @object = "event",
            type,
            created = 1_700_000_000,
            data = new { @object = sessionObject },
        };

        return JsonSerializer.Serialize(evt);
    }

    [Fact]
    public async Task Buy_PremiumBundle_OpensCheckoutSession_AndIssuesNoLicense()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            const string bundleId = "test/checkout-premium";
            const string userId = "buyer-1";
            SeedPremiumBundle(brain, bundleId);

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");
            var buy = await market.BuyBundleAsync(bundleId, userId);

            buy.Success.Should().BeTrue(buy.ErrorMessage);
            buy.CheckoutSessionId.Should().NotBeNullOrEmpty("a premium buy must open a Stripe Checkout session");
            buy.CheckoutUrl.Should().NotBeNullOrEmpty();
            buy.LicenseToken.Should().BeEmpty("no license may be granted before payment is confirmed");

            var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
            (await db.SelectLicensesAsync(userId, bundleId)).Should().BeEmpty();
            (await db.SelectPurchasesAsync(userId, bundleId)).Should().BeEmpty();
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConfirmCheckout_CompletedWebhook_RecordsPurchase_AndIssuesVerifiableLicense()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            const string bundleId = "test/checkout-fulfill";
            const string userId = "buyer-2";
            SeedPremiumBundle(brain, bundleId);

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");

            var buy = await market.BuyBundleAsync(bundleId, userId);
            var sessionId = buy.CheckoutSessionId;

            var confirm = await market.ConfirmCheckoutAsync(CompletedEvent(bundleId, userId, sessionId), stripeSignature: "");

            confirm.Success.Should().BeTrue(confirm.Message);
            confirm.LicenseToken.Should().NotBeNullOrEmpty();

            var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
            (await db.SelectPurchasesAsync(userId, bundleId)).Should().NotBeEmpty("a confirmed payment records the purchase");
            (await db.SelectLicensesAsync(userId, bundleId)).Should().NotBeEmpty();

            var license = brain.GrainFactory.GetGrain<ILicenseNeuron>("license-server");
            (await license.VerifyLicenseAsync(confirm.LicenseToken, bundleId, userId))
                .Should().BeTrue("the issued license must verify cryptographically and against the entitlement table");
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConfirmCheckout_NonCompletedEvent_IssuesNoLicense()
    {
        var brain = await TestDigitalBrain.StartAsync(o => o.WithMockedLlm());
        try
        {
            const string bundleId = "test/checkout-unconfirmed";
            const string userId = "buyer-3";
            SeedPremiumBundle(brain, bundleId);

            var market = brain.GrainFactory.GetGrain<IMarketplaceNeuron>("test-marketplace");
            await market.BuyBundleAsync(bundleId, userId);

            // An expired (i.e. unpaid) checkout must not entitle the buyer.
            var confirm = await market.ConfirmCheckoutAsync(ExpiredEvent(bundleId, userId, "cs_expired"), stripeSignature: "");

            confirm.Success.Should().BeFalse("an unconfirmed/failed payment grants no license");
            confirm.LicenseToken.Should().BeEmpty();

            var db = brain.GrainFactory.GetGrain<IPostgresDbNeuron>("marketplace-db");
            (await db.SelectLicensesAsync(userId, bundleId)).Should().BeEmpty();
        }
        finally
        {
            await brain.DisposeAsync();
        }
    }
}
