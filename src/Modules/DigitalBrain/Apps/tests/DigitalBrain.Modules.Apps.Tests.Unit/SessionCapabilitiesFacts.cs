using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class SessionCapabilitiesFacts
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task DeveloperModeReflectsWhetherTheComposedSandboxCanRun(bool? canRun, bool expected)
    {
        await using var brain = await ModuleTest.Create().WithModule<AppsModule>().StartAsync(TestContext.Current.CancellationToken);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton<MarketplaceService>();
        builder.Services.AddSingleton<AppAuthoringPolicy>();
        if (canRun.HasValue) { builder.Services.AddSingleton<IScriptSandbox>(new Sandbox(canRun.Value)); }
        await using var app = builder.Build();
        new AppsModule().Configure(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(route => route.RoutePattern.RawText == "/session/capabilities");
        var context = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = TestContext.Current.CancellationToken };
        using var body = new MemoryStream();
        context.Response.Body = body;
        await endpoint.RequestDelegate!(context);
        using var payload = JsonDocument.Parse(body.ToArray());
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal(expected, payload.RootElement.GetProperty("developerMode").GetBoolean());
    }

    private sealed class Sandbox(bool canRun) : IScriptSandbox
    {
        public bool CanRun => canRun;
        public string AuthoringDescription => "External script process";
        public Task<ScriptContractCatalog> ReadContracts(IReadOnlyList<string> modules, CancellationToken cancellationToken)
            => Task.FromResult(new ScriptContractCatalog([], [], ""));
        public ScriptCompilationCheck Check(IReadOnlyDictionary<string, string> files) => new(true, []);
    }
}
