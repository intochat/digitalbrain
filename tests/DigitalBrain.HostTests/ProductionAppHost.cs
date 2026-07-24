using System.Net;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class ProductionAppHost(ProductionAppHostFixture fixture)
{
    [Fact(DisplayName = "the production AppHost serves its website and exposes module resources")]
    public async Task ProductionAppHostServesItsWebsiteAndExposesModuleResources()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var storage = host.Resource("brain-storage");
        var ai = host.Resource("brain-ai-ollama");
        var silo = host.Resource("silo");
        var mcp = host.Resource("digitalbrain-mcp");
        var google = host.Resource("google-client-id");
        var salesforce = host.Resource("salesforce-client-id");
        var website = host.Resource("website");

        await storage.WaitUntilHealthyAsync(cancellationToken);
        await ai.WaitUntilHealthyAsync(cancellationToken);
        await silo.WaitUntilHealthyAsync(cancellationToken);
        await mcp.WaitUntilHealthyAsync(cancellationToken);
        await website.WaitUntilHealthyAsync(cancellationToken);

        using var siloClient = silo.CreateHttpClient();
        using var siloHealth = await siloClient.GetAsync(
            new Uri("/health", UriKind.Relative),
            cancellationToken);
        using var mcpClient = mcp.CreateHttpClient();
        using var mcpHealth = await mcpClient.GetAsync(
            new Uri("/health", UriKind.Relative),
            cancellationToken);
        using var websiteClient = website.CreateHttpClient();
        using var websiteHome = await websiteClient.GetAsync(
            new Uri("/", UriKind.Relative),
            cancellationToken);

        Assert.Equal("google-client-id", google.Name);
        Assert.Equal("salesforce-client-id", salesforce.Name);
        Assert.Equal(HttpStatusCode.OK, siloHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mcpHealth.StatusCode);
        Assert.Equal(HttpStatusCode.OK, websiteHome.StatusCode);
    }
}
