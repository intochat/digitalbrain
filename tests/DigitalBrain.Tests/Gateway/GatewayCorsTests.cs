using DigitalBrain.Tests.TestSupport;

namespace DigitalBrain.Tests.Gateway;

[Collection("kernel-host")]
public class GatewayCorsTests(KernelWebApplicationFactory factory)
{
    private readonly KernelWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Preflight_FromBrowserOrigin_AllowsOriginOnGrpcRoute()
    {
        var client = _factory.CreateClient();
        using var preflight = new HttpRequestMessage(
            HttpMethod.Options, "/digitalbrain.DigitalBrainGateway/Health");
        preflight.Headers.Add("Origin", "https://digitalbrain.tech");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-grpc-web");

        var response = await client.SendAsync(preflight);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains("https://digitalbrain.tech", origins);
    }
}
