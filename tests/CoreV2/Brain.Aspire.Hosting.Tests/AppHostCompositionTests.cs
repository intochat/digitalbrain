using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Brain.Aspire.Hosting.Tests;

public sealed class AppHostCompositionTests
{
    [Fact]
    public async Task AppHost_declares_runtime_and_client_with_correct_dependencies()
    {
        using var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.DigitalBrain_AppHost>(TestContext.Current.CancellationToken);

        var resources = appHost.Resources.ToDictionary(resource => resource.Name);
        Assert.Contains("runtime", resources.Keys);
        Assert.Contains("product", resources.Keys);
        Assert.Contains("flutter", resources.Keys);
        Assert.Single(resources["product"].Annotations.OfType<McpServerEndpointAnnotation>());
        Assert.Contains(
            resources["product"].Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource.Name == "runtime");
        Assert.Contains(
            resources["flutter"].Annotations.OfType<WaitAnnotation>(),
            annotation => annotation.Resource.Name == "product"
                && annotation.WaitType == WaitType.WaitUntilHealthy);
    }
}
