using Aspire.Hosting.Testing;

namespace IntoChat.Tests.E2E.Security;

// P2.1: the product profile must not compose the CSharp module. A C# file is the only program that
// opens an Orleans gateway connection (DigitalBrainClient.ConnectAsync), so leaving the module out of
// the product profile is what keeps an arbitrary program outside the cluster. The developer profile
// keeps it for local authoring.
public sealed class WorkerGatewayDeniedFacts
{
    [Fact]
    public async Task ProductProfileComposesNoCSharpModule()
    {
        var names = await ResourceNames(["IntoChat:Profile=product"], TestContext.Current.CancellationToken);
        Assert.DoesNotContain("CSharp", names);
    }

    [Fact]
    public async Task DeveloperProfileKeepsTheCSharpModule()
    {
        var names = await ResourceNames(["IntoChat:Profile=developer"], TestContext.Current.CancellationToken);
        Assert.Contains("CSharp", names);
    }

    private static async Task<string[]> ResourceNames(string[] args, CancellationToken cancellationToken)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(args, cancellationToken);
        return appHost.Resources.Select(resource => resource.Name).ToArray();
    }
}
