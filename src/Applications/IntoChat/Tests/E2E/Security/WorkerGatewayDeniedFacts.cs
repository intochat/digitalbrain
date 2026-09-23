using Aspire.Hosting.Testing;

namespace IntoChat.Tests.E2E.Security;

// P2.1: the product profile must not compose a behavior worker. A behavior worker is the only
// process that opens an Orleans gateway connection (it calls UseOrleansClient in BehaviorApp), so
// denying the worker in the product profile is what keeps an arbitrary program outside the
// cluster. The developer profile keeps it for local authoring.
public sealed class WorkerGatewayDeniedFacts
{
    [Fact]
    public async Task ProductProfileComposesNoBehaviorWorker()
    {
        var names = await ResourceNames(["IntoChat:Profile=product"], TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Behavior", names);
    }

    [Fact]
    public async Task DeveloperProfileKeepsTheBehaviorWorker()
    {
        var names = await ResourceNames(["IntoChat:Profile=developer"], TestContext.Current.CancellationToken);
        Assert.Contains("Behavior", names);
    }

    private static async Task<string[]> ResourceNames(string[] args, CancellationToken cancellationToken)
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IntoChat_AppHost>(args, cancellationToken);
        return appHost.Resources.Select(resource => resource.Name).ToArray();
    }
}
