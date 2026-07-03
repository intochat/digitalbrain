using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceAuthNeuronTests : NeuronTestBase
{
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
}
