using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;

namespace DigitalBrain.Tests.Kernel;

[Collection("kernel-host")]
public class KernelGrpcWebTests(KernelWebApplicationFactory factory)
{
    private readonly KernelWebApplicationFactory _factory = factory;

    [Fact]
    public async Task Health_Over_GrpcWeb_Succeeds()
    {
        var handler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, _factory.Server.CreateHandler());
        using var channel = GrpcChannel.ForAddress(_factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = handler });
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var reply = await client.HealthAsync(new HealthRequest());
        Assert.True(reply.Ok);
    }
}
