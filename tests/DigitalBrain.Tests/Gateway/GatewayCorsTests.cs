using DigitalBrain.Tests.TestSupport;

namespace DigitalBrain.Tests.Gateway;

[Collection("kernel-host")]
public class GatewayCorsTests(KernelWebApplicationFactory factory)
{
    private readonly KernelWebApplicationFactory _factory = factory;

    [Theory]
    [InlineData("https://digitalbrain.tech")]
    [InlineData("https://www.digitalbrain.tech")]
    public async Task Preflight_FromBrowserOrigin_AllowsOriginOnGrpcRoute(string origin)
    {
        var client = _factory.CreateClient();
        using var preflight = new HttpRequestMessage(
            HttpMethod.Options, "/digitalbrain.DigitalBrainGateway/Health");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-grpc-web");

        var response = await client.SendAsync(preflight);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Contains(origin, origins);
    }
}
