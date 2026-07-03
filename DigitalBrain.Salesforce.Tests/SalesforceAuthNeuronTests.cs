using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceAuthNeuronTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddPackConfigStore(blobsForKeyRing: null));

    [Fact]
    public async Task AuthRequested_Emits_Credential_Form()
    {
        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test");
        await auth.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-1"
        })
        { Receiver = new NeuronId("salesforce-auth-test") });

        var outgoing = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(outgoing.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal(SalesforceClientFactory.PackName, form.Props["pack"]);
        Assert.Equal("session-1", form.Props["sessionId"]);

        var tree = Assert.IsType<UiWidgetTree>(form.Props["tree"]);
        var fields = FindNodes(tree)
            .Where(node => node.Type == UiKitVocabulary.TextField)
            .Select(node => node.Props)
            .ToList();

        Assert.Contains(fields, field => Equals(field["name"], SalesforceClientFactory.ClientIdKey));
        Assert.Contains(fields, field => Equals(field["name"], SalesforceClientFactory.PasswordKey) && Equals(field["secret"], true));
        Assert.Contains(fields, field => Equals(field["name"], SalesforceClientFactory.SecurityTokenKey) && Equals(field["secret"], true));

        var button = FindNodes(tree).Single(node =>
            node.Type == UiKitVocabulary.Button &&
            Equals(node.Props["synapseType"], SalesforceSignals.AuthRequested));
        Assert.Equal("Login via Salesforce", button.Props["label"]);
        Assert.Equal(SalesforceClientFactory.DefaultCallbackPath, button.Props["callbackPath"]);
    }

    [Fact]
    public async Task OAuthStart_Uses_Stored_Connected_App_Config()
    {
        var writer = Grain<ISalesforceConnectedAppConfigWriter>("salesforce-connected-app-writer");
        await writer.StoreConnectedAppConfigAsync();

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test");
        await auth.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-oauth",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("salesforce-auth-test") });

        var outgoing = await auth.GetOutgoingTimelineAsync();
        var signal = Assert.Single(outgoing.OfType<Signal>(), item => item.Name == SalesforceSignals.AuthUrl);
        var url = Assert.IsType<string>(signal.Props["url"]);

        Assert.StartsWith("https://test.salesforce.com/services/oauth2/authorize?", url);
        Assert.Contains("client_id=connected-app-id", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A8081%2Fsalesforce-callback", url);
        Assert.Contains("code_challenge=", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact]
    public async Task OAuthStart_Without_Connected_App_Config_Emits_Clear_Credential_Form()
    {
        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test");
        await auth.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-oauth-missing",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("salesforce-auth-test") });

        var outgoing = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(outgoing.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal("session-oauth-missing", form.Props["sessionId"]);

        var tree = Assert.IsType<UiWidgetTree>(form.Props["tree"]);
        Assert.Contains("Salesforce OAuth is not configured", FlattenText(tree));
    }

    private static IEnumerable<UiWidgetTree> FindNodes(UiWidgetTree tree)
    {
        yield return tree;

        foreach (var child in tree.Children ?? [])
        {
            foreach (var found in FindNodes(child))
                yield return found;
        }
    }

    private static string FlattenText(UiWidgetTree tree)
    {
        var values = new List<string>();
        Collect(tree);
        return string.Join("\n", values);

        void Collect(UiWidgetTree node)
        {
            if (node.Props.TryGetValue("text", out var text) && text is not null)
                values.Add(text.ToString()!);

            foreach (var child in node.Children ?? [])
                Collect(child);
        }
    }
}

public interface ISalesforceConnectedAppConfigWriter : INeuron
{
    Task StoreConnectedAppConfigAsync();
}

[GrainType("digitalbrain.test.salesforce-connected-app-writer")]
public sealed class SalesforceConnectedAppConfigWriter(
    Microsoft.Extensions.Logging.ILogger<SalesforceConnectedAppConfigWriter> logger,
    NeuronJournals journals)
    : Neuron(logger, journals), ISalesforceConnectedAppConfigWriter
{
    public Task StoreConnectedAppConfigAsync() =>
        ServiceProvider.GetRequiredService<IPackConfigStore>().SetAsync(
            SalesforceClientFactory.DefaultScope,
            SalesforceClientFactory.PackName,
            new Dictionary<string, string>
            {
                [SalesforceClientFactory.ClientIdKey] = "connected-app-id",
                [SalesforceClientFactory.ClientSecretKey] = "connected-app-secret",
                [SalesforceClientFactory.LoginUrlKey] = "https://test.salesforce.com"
            });
}
