using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Pack.Contracts;
using DigitalBrain.Tests.Ino;
using DigitalBrain.TestKit;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Google;

public class GmailNeuronEnsureConnectedTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddPackConfigStore(blobsForKeyRing: null));

    [Fact]
    public async Task Returns_false_and_delivers_login_surface_when_no_session()
    {
        var gmail = Grain<IGmailNeuron>("gmail-capability-main");

        var connected = await gmail.EnsureConnectedAsync("session-no-login");

        Assert.False(connected);
        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surfaces = (await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>().ToList();
        Assert.Contains(surfaces, s =>
            s.Kind == UiSurfaceKinds.Login &&
            Equals(s.Props.GetValueOrDefault("clientId"), "session-no-login"));
    }

    [Fact]
    public async Task Returns_false_and_delivers_credential_form_when_signed_in_without_google_credential()
    {
        const string clientId = "session-signed-in-no-cred";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("gmail-cred-user", "correct horse battery staple", clientId));

        var gmail = Grain<IGmailNeuron>("gmail-capability-main");
        var connected = await gmail.EnsureConnectedAsync(clientId);

        Assert.False(connected);
        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surfaces = (await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>().ToList();
        Assert.Contains(surfaces, s =>
            s.Kind == ConfigFormSurface.Kind &&
            Equals(s.Props.GetValueOrDefault("pack"), GoogleClientFactory.PackName) &&
            Equals(s.Props.GetValueOrDefault("clientId"), clientId));
    }

    [Fact]
    public async Task Returns_true_when_signed_in_with_google_credential()
    {
        const string clientId = "session-signed-in-with-cred";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("gmail-connected-user", "correct horse battery staple", clientId));

        var config = Grain<IGoogleConfigWriter>("google-config-writer");
        await config.StoreGoogleCredentialAsync();

        var gmail = Grain<IGmailNeuron>("gmail-capability-main");
        var connected = await gmail.EnsureConnectedAsync(clientId);

        Assert.True(connected);
    }
}
