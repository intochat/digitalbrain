using DigitalBrain.Kernel.Hosting;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.UseDigitalBrainOrleans();
builder.AddDigitalBrainClients();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    if (builder.Environment.IsProduction() && string.Equals(
            builder.Configuration["DigitalBrain:Runtime:ForwardedHeaders:TrustAzureContainerAppsIngress"],
            "true",
            StringComparison.OrdinalIgnoreCase))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    }
});

builder.ConfigureDigitalBrainKestrel();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<OAuthTransportBoundary>();
app.MapDigitalBrainSetup();
app.MapConnectorOAuthCallbacks();

app.Run();
