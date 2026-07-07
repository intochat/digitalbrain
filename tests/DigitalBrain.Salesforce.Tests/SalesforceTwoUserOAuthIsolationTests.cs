using DigitalBrain.Core;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceTwoUserOAuthIsolationTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<HttpMessageHandler>(
                new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com"));
        });

    [Fact]
    public async Task Two_Users_Interleaved_OAuth_Flows_Do_Not_Cross_Contaminate()
    {
        var writer = Grain<ISalesforceConnectedAppConfigWriter>("salesforce-connected-app-writer-two-user");
        await writer.StoreConnectedAppConfigAsync();

        var alice = Grain<ISalesforceAuthNeuron>("alice");
        var bob = Grain<ISalesforceAuthNeuron>("bob");

        await alice.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["clientId"] = "session-alice",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/oauth/callback/salesforce"
        })
        { Receiver = new NeuronId("alice") });

        await bob.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["clientId"] = "session-bob",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/oauth/callback/salesforce"
        })
        { Receiver = new NeuronId("bob") });

        var aliceAuthUrl = Assert.Single((await alice.GetOutgoingTimelineAsync()).OfType<Signal>(), s => s.Name == SalesforceSignals.AuthUrl);
        var bobAuthUrl = Assert.Single((await bob.GetOutgoingTimelineAsync()).OfType<Signal>(), s => s.Name == SalesforceSignals.AuthUrl);
        var aliceState = FakeSalesforceTokenHandler.ExtractQueryValue((string)aliceAuthUrl.Props["url"]!, "state");
        var bobState = FakeSalesforceTokenHandler.ExtractQueryValue((string)bobAuthUrl.Props["url"]!, "state");

        Assert.StartsWith("alice:", aliceState);
        Assert.StartsWith("bob:", bobState);

        // Wrong-user callback: Alice's grain, Bob's state — fails closed, no exchange, no cross-write.
        var crossResult = await alice.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "some-code", State: bobState, Error: null, ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/salesforce"));
        Assert.False(crossResult.Success);
        Assert.Equal("The callback state did not match the pending login.", crossResult.Message);

        var aliceResult = await alice.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "alice-code", State: aliceState, Error: null, ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/salesforce"));
        Assert.True(aliceResult.Success);

        // Presence/absence check (not value equality, which the constant-valued fake token handler can't
        // discriminate between users): if Alice's completion had collapsed into Bob's scope, Bob's scope would
        // already show a token here, before Bob has even completed his own flow.
        var bobTokensBeforeBobCompletes = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob")), SalesforceClientFactory.PackName);
        Assert.False(bobTokensBeforeBobCompletes.ContainsKey(SalesforceClientFactory.AccessTokenKey));

        var bobResult = await bob.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "bob-code", State: bobState, Error: null, ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/salesforce"));
        Assert.True(bobResult.Success);

        var aliceTokens = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("alice")), SalesforceClientFactory.PackName);
        var bobTokens = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob")), SalesforceClientFactory.PackName);
        Assert.Equal("fake-access-token", aliceTokens[SalesforceClientFactory.AccessTokenKey]);
        Assert.Equal("fake-access-token", bobTokens[SalesforceClientFactory.AccessTokenKey]);

        // Neither user's pending PKCE state is readable from the other's scope.
        var alicePendingFromBobScope = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob")), SalesforceClientFactory.OAuthPendingPackName);
        Assert.False(alicePendingFromBobScope.ContainsKey(SalesforceClientFactory.OAuthStateKey) && string.Equals(alicePendingFromBobScope.GetValueOrDefault(SalesforceClientFactory.OAuthStateKey), aliceState));
    }
}
