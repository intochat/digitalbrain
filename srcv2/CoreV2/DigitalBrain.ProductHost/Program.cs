using DigitalBrain.Aspire;
using DigitalBrain.ProductHost.Mcp;
using DigitalBrain.ProductHost.Protocol;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainClient();
builder.Services.AddSingleton<IProductRuntimeClient, OrleansProductRuntimeClient>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services
    .AddMcpServer()
    .WithHttpTransport(static options => options.Stateless = true)
    .WithTools<ProductMcpTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCors();
app.MapProductProtocol();
app.MapMcp("/mcp");
app.Run();
