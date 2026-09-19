using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleEndpointFacts
{
    [Fact]
    public async Task ModuleEndpointIsMappedOntoTheHttpHost()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(new()
        {
            Modules = [new EchoModule()],
            UseHttp = true,
        }, ct);
        Assert.Equal("hello", await brain.Http().GetStringAsync("echo/hello", ct));
    }

    private sealed class EchoModule : IModule
    {
        public void Configure(ISiloBuilder silo) { }

        public void Configure(IEndpointRouteBuilder endpoints)
            => endpoints.MapGet("echo/{word}", (string word) => word);
    }
}
