using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleAuthNeuronTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<HttpMessageHandler>(
                new FakeGoogleTokenHandler("fake-google-access-token", "fake-google-refresh-token"));
        });

    [Fact]
    public async Task AuthRequested_Fires_AuthUrl_WithCorrectParams()
    {
        var config = Grain<IGoogleConfigWriter>("google-config-writer");
        await config.StoreConnectedAppConfigAsync();

        var auth = Grain<IGoogleAuthNeuron>("google-auth-test");
        await auth.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("google-auth-test") });

        var outgoing = await auth.GetTimelineAsync();
        var authUrlSignal = Assert.Single(outgoing.OfType<Signal>(), s => s.Name == GoogleSignals.AuthUrl);
        var url = (string)authUrlSignal.Props["url"]!;
        Assert.Contains("access_type=offline", url);
        Assert.Contains("prompt=consent", url);
        Assert.Contains("scope=https%3A%2F%2Fwww.googleapis.com%2Fauth%2Fgmail.readonly", url);
        Assert.Contains("client_id=test-client-id.apps.googleusercontent.com", url);
    }

    [Fact]
    public async Task CompleteOAuthAsync_With_Valid_Code_And_State_Stores_UserScoped_Tokens_And_Clears_Pending()
    {
        var writer = Grain<IGoogleConfigWriter>("google-config-writer-complete");
        await writer.StoreConnectedAppConfigAsync();

        var auth = Grain<IGoogleAuthNeuron>("google-auth-test-complete");
        await auth.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("google-auth-test-complete") });

        var authUrlSignal = Assert.Single((await auth.GetTimelineAsync()).OfType<Signal>(), s => s.Name == GoogleSignals.AuthUrl);
        var state = FakeGoogleTokenHandler.ExtractQueryValue((string)authUrlSignal.Props["url"]!, "state");

        var result = await auth.CompleteOAuthAsync(new GoogleOAuthCallback(
            Code: "auth-code-1",
            State: state,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/google"));

        Assert.True(result.Success);
        Assert.Equal("Google connected", result.Title);

        var stored = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("google-auth-test-complete")), GoogleClientFactory.PackName);
        Assert.Equal("fake-google-access-token", stored[GoogleClientFactory.AccessTokenKey]);
        Assert.Equal("fake-google-refresh-token", stored[GoogleClientFactory.RefreshTokenKey]);
        Assert.Equal("test-client-id.apps.googleusercontent.com", stored[GoogleClientFactory.ClientIdKey]);

        var pendingAfter = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("google-auth-test-complete")), GoogleClientFactory.OAuthPendingPackName);
        Assert.False(pendingAfter.ContainsKey(GoogleClientFactory.OAuthStateKey));
    }

    [Fact]
    public async Task CompleteOAuthAsync_With_Mismatched_State_Fails_Without_Exchanging_Code()
    {
        var writer = Grain<IGoogleConfigWriter>("google-config-writer-mismatch");
        await writer.StoreConnectedAppConfigAsync();

        var auth = Grain<IGoogleAuthNeuron>("google-auth-test-mismatch");
        await auth.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("google-auth-test-mismatch") });

        var result = await auth.CompleteOAuthAsync(new GoogleOAuthCallback(
            Code: "auth-code-1",
            State: "wrong-state",
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/google"));

        Assert.False(result.Success);
        Assert.Equal("The callback state did not match the pending login.", result.Message);

        var stored = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("google-auth-test-mismatch")), GoogleClientFactory.PackName);
        Assert.False(stored.ContainsKey(GoogleClientFactory.AccessTokenKey));
    }

    [Fact]
    public async Task CompleteOAuthAsync_With_No_Pending_Flow_Fails_Without_Exchanging_Code()
    {
        var writer = Grain<IGoogleConfigWriter>("google-config-writer-no-pending");
        await writer.StoreConnectedAppConfigAsync();

        var auth = Grain<IGoogleAuthNeuron>("google-auth-test-no-pending");

        var result = await auth.CompleteOAuthAsync(new GoogleOAuthCallback(
            Code: "auth-code-1",
            State: "some-state",
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/google"));

        Assert.False(result.Success);
        Assert.Equal("The callback state did not match the pending login.", result.Message);

        var stored = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("google-auth-test-no-pending")), GoogleClientFactory.PackName);
        Assert.False(stored.ContainsKey(GoogleClientFactory.AccessTokenKey));
    }

    [Fact]
    public async Task Two_Users_Interleaved_OAuth_Flows_Do_Not_Cross_Contaminate()
    {
        var writer = Grain<IGoogleConfigWriter>("google-config-writer-two-user");
        await writer.StoreConnectedAppConfigAsync();

        var alice = Grain<IGoogleAuthNeuron>("alice-google");
        var bob = Grain<IGoogleAuthNeuron>("bob-google");

        await alice.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("alice-google") });
        await bob.DeliverAsync(new Signal(GoogleSignals.AuthRequested, new Dictionary<string, object?>())
        { Receiver = new NeuronId("bob-google") });

        var aliceAuthUrl = Assert.Single((await alice.GetTimelineAsync()).OfType<Signal>(), s => s.Name == GoogleSignals.AuthUrl);
        var bobAuthUrl = Assert.Single((await bob.GetTimelineAsync()).OfType<Signal>(), s => s.Name == GoogleSignals.AuthUrl);
        var aliceState = FakeGoogleTokenHandler.ExtractQueryValue((string)aliceAuthUrl.Props["url"]!, "state");
        var bobState = FakeGoogleTokenHandler.ExtractQueryValue((string)bobAuthUrl.Props["url"]!, "state");

        Assert.StartsWith("alice-google:", aliceState);
        Assert.StartsWith("bob-google:", bobState);

        var crossResult = await alice.CompleteOAuthAsync(new GoogleOAuthCallback(
            Code: "bob-code",
            State: bobState,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/google"));
        Assert.False(crossResult.Success);

        var aliceResult = await alice.CompleteOAuthAsync(new GoogleOAuthCallback(
            Code: "alice-code",
            State: aliceState,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/google"));
        Assert.True(aliceResult.Success);

        var bobTokensBeforeBobCompletes = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob-google")), GoogleClientFactory.PackName);
        Assert.False(bobTokensBeforeBobCompletes.ContainsKey(GoogleClientFactory.AccessTokenKey));

        var bobResult = await bob.CompleteOAuthAsync(new GoogleOAuthCallback(
            Code: "bob-code",
            State: bobState,
            Error: null,
            ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/oauth/callback/google"));
        Assert.True(bobResult.Success);

        var aliceTokens = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("alice-google")), GoogleClientFactory.PackName);
        var bobTokens = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob-google")), GoogleClientFactory.PackName);
        Assert.Equal("fake-google-access-token", aliceTokens[GoogleClientFactory.AccessTokenKey]);
        Assert.Equal("fake-google-access-token", bobTokens[GoogleClientFactory.AccessTokenKey]);
    }
}

[Alias("DigitalBrain.Google.Tests.IGoogleConfigWriter")]
public interface IGoogleConfigWriter : INeuron
{
    [Alias("StoreConnectedAppConfigAsync")]
    Task StoreConnectedAppConfigAsync();
    [Alias("ReadPackAsync")]
    Task<IReadOnlyDictionary<string, string>> ReadPackAsync(string scope, string pack);
}

[GrainType("digitalbrain.test.google-config-writer")]
public sealed class GoogleConfigWriter(ILogger<GoogleConfigWriter> logger, NeuronJournals journals)
    : Neuron(logger, journals), IGoogleConfigWriter
{
    public Task StoreConnectedAppConfigAsync() =>
        ServiceProvider.GetRequiredService<IPackConfigStore>().SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, new Dictionary<string, string>
        {
            [GoogleClientFactory.ClientIdKey] = "test-client-id.apps.googleusercontent.com",
            [GoogleClientFactory.ClientSecretKey] = "test-secret",
            [GoogleClientFactory.RedirectUriKey] = "http://localhost:8081/oauth/callback/google"
        });

    public Task<IReadOnlyDictionary<string, string>> ReadPackAsync(string scope, string pack) =>
        ServiceProvider.GetRequiredService<IPackConfigStore>().GetAsync(scope, pack);
}
