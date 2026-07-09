using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Salesforce;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.Pack.Contracts;
using DigitalBrain.Tests.Ino;
using DigitalBrain.TestKit;
using DigitalBrain.Ui.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Salesforce;

public class SalesforceCrmNeuronEnsureConnectedTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddPackConfigStore(blobsForKeyRing: null));

    [Fact]
    public async Task Returns_false_and_delivers_login_surface_when_no_session()
    {
        var sf = Grain<ISalesforceCrmNeuron>("salesforce-capability-main");

        var connected = await sf.EnsureConnectedAsync("session-sf-no-login");

        Assert.False(connected);
        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surfaces = (await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>().ToList();
        Assert.Contains(surfaces, s =>
            s.Kind == UiSurfaceKinds.Login &&
            Equals(s.Props.GetValueOrDefault("clientId"), "session-sf-no-login"));
    }

    [Fact]
    public async Task Returns_false_and_delivers_credential_form_when_signed_in_without_salesforce_credential()
    {
        const string clientId = "session-sf-signed-in-no-cred";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("sf-cred-user", "correct horse battery staple", clientId));

        var sf = Grain<ISalesforceCrmNeuron>("salesforce-capability-main");
        var connected = await sf.EnsureConnectedAsync(clientId);

        Assert.False(connected);
        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var surfaces = (await flutter.GetIncomingTimelineAsync()).OfType<UiSurface>().ToList();
        Assert.Contains(surfaces, s =>
            s.Kind == ConfigFormSurface.Kind &&
            Equals(s.Props.GetValueOrDefault("pack"), SalesforceClientFactory.PackName) &&
            Equals(s.Props.GetValueOrDefault("clientId"), clientId));
    }

    [Fact]
    public async Task Returns_true_when_signed_in_with_salesforce_credential()
    {
        const string clientId = "session-sf-signed-in-with-cred";
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("sf-connected-user", "correct horse battery staple", clientId));

        var config = Grain<ISalesforceConfigWriter>("salesforce-config-writer");
        await config.StoreSalesforceCredentialAsync();

        var sf = Grain<ISalesforceCrmNeuron>("salesforce-capability-main");
        var connected = await sf.EnsureConnectedAsync(clientId);

        Assert.True(connected);
    }
}
