using DigitalBrain.Core;
using DigitalBrain.TestKit;
using DigitalBrain.Kernel;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Google;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Google.Tests;

public class GoogleAuthNeuronTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddPackConfigStore(blobsForKeyRing: null));

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
}

public interface IGoogleConfigWriter : INeuron
{
    Task StoreConnectedAppConfigAsync();
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
            [GoogleClientFactory.RedirectUriKey] = "http://localhost:51014/google-callback"
        });
}
