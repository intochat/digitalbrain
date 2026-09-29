using Aspire.Hosting.Testing;

namespace IntoChat.Tests.E2E.Security;

// CSharp is composed for every profile. A C# file is an arbitrary program that calls the brain
// through the script edge; running files needs the host's C# sandbox.
public sealed class WorkerGatewayDeniedFacts
{
    [Fact]
    public async Task ProductProfileComposesTheCSharpModule()
    {
        var names = await ResourceNames(["IntoChat:Profile=product"], TestContext.Current.CancellationToken);
        Assert.Contains("CSharp", names);
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
