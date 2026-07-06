using DigitalBrain.Tests.TestSupport;

namespace DigitalBrain.Tests.Kernel;

[Collection("kernel-host")]
public class HealthEndpointTests(KernelWebApplicationFactory factory)
{
    private readonly KernelWebApplicationFactory _factory = factory;

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
