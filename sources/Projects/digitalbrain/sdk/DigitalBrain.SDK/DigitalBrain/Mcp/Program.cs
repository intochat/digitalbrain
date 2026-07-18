using DigitalBrain.Runtime.Grpc;
using Grpc.Net.Client;
using DigitalBrain.Runtime;
using DigitalBrain.SDK.DigitalBrain.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var endpoint = Environment.GetEnvironmentVariable("KERNEL_ENDPOINT")
    ?? "https://localhost:7000";
Console.WriteLine($"[MCP] KERNEL_ENDPOINT = {endpoint}");

builder.Services.AddSingleton(_ =>
{
    var handler = new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    };
    return GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { HttpHandler = handler });
});

builder.Services.AddSingleton(sp =>
    new DigitalBrainGateway.DigitalBrainGatewayClient(sp.GetRequiredService<GrpcChannel>()));

builder.Services.AddSingleton(sp =>
    new BrainWatch.BrainWatchClient(sp.GetRequiredService<GrpcChannel>()));

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<BrainTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");
app.Run();
