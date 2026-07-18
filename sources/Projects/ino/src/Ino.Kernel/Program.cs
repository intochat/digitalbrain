using Ino.Core.Hosting.Llm;
using Ino.Gateway.Grpc;
using Ino.Kernel;
using Ino.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKernel();
builder.AddInoChatClients();
builder.AddInoGrpcGateway();

builder.Services.AddControllers();
builder.Services.Configure<MarketplaceControllerOptions>(o =>
{
    var installedOverride = Environment.GetEnvironmentVariable("INO_INSTALLED_JSON_PATH");
    if (!string.IsNullOrWhiteSpace(installedOverride))
        o.InstalledStatePath = installedOverride;

    var feedOverride = Environment.GetEnvironmentVariable("INO_MARKETPLACE_JSON_PATH");
    if (!string.IsNullOrWhiteSpace(feedOverride))
        o.MarketplaceFeedPath = feedOverride;
});

// Phase 2: use Null restart service. A real Aspire-backed restart bridge is
// deferred — ResourceCommandService is AppHost-scoped and not injectable into
// a ProjectResource's DI. See Task 20 for the AppHost-side restart path.
builder.Services.AddSingleton<IDomainRestartService, NullDomainRestartService>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapControllers();

// Gateway gRPC + gRPC-Web + static file serving for the Flutter bundle
// when it's been copied into wwwroot (slice 1.5 build step). The SPA
// fallback inside UseInoGrpcGateway serves index.html for unmatched
// routes — GoRouter on the Flutter side picks up "?q=..." deep links.
app.UseInoGrpcGateway(wwwroot: "wwwroot");

await app.RunAsync();
