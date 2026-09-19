using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleEndpointFacts
{
    [Fact]
    public async Task MapsRoutesRegisteredByModules()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IModule, ProbeModule>();
        await using var app = builder.Build();
        app.MapModuleEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal("ok", await client.GetStringAsync("/from-module", TestContext.Current.CancellationToken));
    }

    private sealed class ProbeModule : IModule
    {
        public void Configure(ISiloBuilder builder) { }

        public void Configure(IEndpointRouteBuilder endpoints)
            => endpoints.MapGet("/from-module", () => "ok");
    }
}
