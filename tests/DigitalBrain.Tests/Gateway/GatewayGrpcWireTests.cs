using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Tests.TestSupport;
using Grpc.Net.Client;

namespace DigitalBrain.Tests.Gateway;

[Collection("kernel-host")]
public class GatewayGrpcWireTests : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly DigitalBrainGateway.DigitalBrainGatewayClient _client;

    public GatewayGrpcWireTests(KernelWebApplicationFactory factory)
    {
        _channel = GrpcChannel.ForAddress(factory.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = factory.Server.CreateHandler()
        });
        _client = new DigitalBrainGateway.DigitalBrainGatewayClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    [Fact]
    public async Task Health_OverGrpc_ReturnsOk()
    {
        var reply = await _client.HealthAsync(new HealthRequest());
        Assert.True(reply.Ok);
    }

    [Fact]
    public async Task Ask_Ino_OverGrpc_ReturnsText()
    {
        var reply = await _client.AskAsync(new AskRequest { NeuronId = "ino-main", Prompt = "hi" });
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    [Fact]
    public async Task Transcribe_OverGrpc_ReturnsCorrelationId()
    {
        using var call = _client.Transcribe();

        await call.RequestStream.WriteAsync(new TranscribeRequest
        {
            MimeType = "audio/wav",
            AudioChunk = global::Google.Protobuf.ByteString.CopyFrom(new byte[] { 1, 2, 3 })
        });
        await call.RequestStream.CompleteAsync();

        var reply = await call.ResponseAsync;

        Assert.Equal(string.Empty, reply.Transcript);
        Assert.False(string.IsNullOrWhiteSpace(reply.CorrelationId));
    }
}
