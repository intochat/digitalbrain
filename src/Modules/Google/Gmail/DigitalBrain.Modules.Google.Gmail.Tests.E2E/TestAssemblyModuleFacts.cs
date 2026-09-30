using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TestAssemblyModuleFacts
{
    [Fact(Timeout = 180_000)]
    public async Task TypedOptionalEndpointReachesExternalRuntime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await E2ETest.Create()
            .WithModule<EndpointModule, EndpointOptions>(o => o.Endpoint = "http://127.0.0.1:8123/v1")
            .StartAsync(ct);
        Assert.Equal("http://127.0.0.1:8123/v1", await brain.HttpClient.GetStringAsync("/configured-endpoint", ct));
        Assert.NotEqual(Environment.ProcessId,
            await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<int>(brain.HttpClient, "/process", ct));
    }
}

public sealed class EndpointModule : IModule<EndpointOptions>
{
    public void Configure(Orleans.Hosting.ISiloBuilder silo) { }
    public void Configure(IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/configured-endpoint",
            (IConfiguration configuration) => configuration.GetModuleOptions<EndpointOptions>(nameof(EndpointModule)).Endpoint ?? "unset");
}
public sealed class EndpointOptions : IModuleOptions
{
    public string? Endpoint { get; set; }
    public void Validate() { }
}
