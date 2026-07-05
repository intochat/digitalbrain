using Microsoft.AspNetCore.Mvc.Testing;

namespace DigitalBrain.Tests.Kernel;

[Collection("silo-host")]
public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AliveEndpoint_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/alive");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
