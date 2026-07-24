using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

[Collection(HostedApplication.CollectionName)]
public sealed class ProductionAppHost
{
    [Fact(DisplayName = "the production AppHost exposes docs as the website resource")]
    public async Task ExposesDocsAsWebsite()
    {
        await using var appHost = await HostedApplication.CreateBuilderAsync<Projects.DigitalBrain_AppHost>(
            TestContext.Current.CancellationToken);
        var website = Assert.Single(appHost.Resources, resource => resource.Name == "website");
        var executable = Assert.IsAssignableFrom<ExecutableResource>(website);
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

        Assert.Equal(Path.Combine(repository, "docs"), executable.WorkingDirectory);
        Assert.Contains(executable.Annotations.OfType<EndpointAnnotation>(), endpoint => endpoint.IsExternal && endpoint.UriScheme == "http");
    }
}
