using DigitalBrain.Core;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceOAuthCrossSiloTests : NeuronTestBase
{
    protected override short InitialSilosCount => 2;

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<HttpMessageHandler>(
                new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com"));
        });

    [Fact]
    public async Task Callback_Delivered_Through_Different_Silo_Frontend_Still_Completes()
    {
        var silo0Grains = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<IGrainFactory>();
        var silo1Grains = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<IGrainFactory>();

        var authOnSilo0 = silo0Grains.GetGrain<ISalesforceAuthNeuron>("salesforce-auth-main");
        var startingSiloIdentity = await authOnSilo0.GetSiloIdentityAsync();

        await authOnSilo0.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["clientId"] = "session-cross-silo",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
            [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
            [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com",
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/oauth/callback/salesforce"
        })
        { Receiver = new NeuronId("salesforce-auth-main") });

        var outgoing = await authOnSilo0.GetOutgoingTimelineAsync();
        var authUrlSignal = Assert.Single(outgoing.OfType<Signal>(), item => item.Name == SalesforceSignals.AuthUrl);
        var authorizeUrl = Assert.IsType<string>(authUrlSignal.Props["url"]);
        var state = FakeSalesforceTokenHandler.ExtractQueryValue(authorizeUrl, "state");

        // Different IGrainFactory, simulating the callback landing on a different Kernel replica
        // than the one that served the "Login via Salesforce" request.
        var authOnSilo1 = silo1Grains.GetGrain<ISalesforceAuthNeuron>("salesforce-auth-main");
        var completingSiloIdentity = await authOnSilo1.GetSiloIdentityAsync();

        // Proves Orleans routed both calls to the SAME single activation regardless of entry silo —
        // this is the property that fixes P1 (previously the callback bypassed the grain entirely).
        Assert.Equal(startingSiloIdentity, completingSiloIdentity);

        var result = await authOnSilo1.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "fake-authorization-code",
            State: state,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/salesforce"));

        Assert.True(result.Success);
        Assert.Equal("Salesforce connected", result.Title);
    }
}
