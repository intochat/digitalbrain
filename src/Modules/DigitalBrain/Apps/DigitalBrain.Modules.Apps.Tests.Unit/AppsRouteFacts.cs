using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Testing.Unit;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using System.Text.Json;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AppsRouteFacts
{
    [Fact]
    public async Task AuthoringRoutesScopeDraftsToTheCallerAndRejectInvalidIdsAndForeignBrains()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        await brain.Get<IAppDrafts>("alice").Record("alice-draft", "Alice", AppDraftStatus.Drafted);
        await brain.Get<IAppDrafts>("bob").Record("bob-draft", "Bob", AppDraftStatus.Drafted);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IDigitalBrain>(brain.SiloServices.GetRequiredService<IDigitalBrain>());
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<MarketplaceService>());
        builder.Services.AddSingleton<IBrainAccess, PersonalBrainAccess>();
        await using var app = builder.Build();
        new AppsModule().Configure(app);
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        foreach (var principal in new[] { "alice", "bob" })
        {
            CallerContextStamper.Stamp(new CallerContext { PrincipalId = principal, AccountId = principal,
                BrainId = principal, Kind = CallerKind.User, StampedBy = TrustedEdge.AuthenticatedHttp });
            var list = await Invoke("/packages/drafts");
            Assert.Equal(200, list.Response.StatusCode);
            list.Response.Body.Position = 0;
            var entries = await JsonSerializer.DeserializeAsync<AppDraftEntry[]>(list.Response.Body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            Assert.Equal(principal + "-draft", Assert.Single(entries!).Id);
            var invalid = await Invoke("/packages/drafts/{id}", ("id", "invalid"));
            Assert.Equal(400, invalid.Response.StatusCode);
            var foreign = await Invoke("/brains/{brainId}/packages/{owner}/{name}/invocations/{invocationId:guid}/discussion",
                ("brainId", principal == "alice" ? "bob" : "alice"), ("owner", principal), ("name", "app"),
                ("invocationId", Guid.NewGuid().ToString()));
            Assert.Equal(403, foreign.Response.StatusCode);
        }

        async Task<DefaultHttpContext> Invoke(string pattern, params (string Key, string Value)[] values)
        {
            var endpoint = endpoints.Single(endpoint => endpoint.RoutePattern.RawText == pattern && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
            var http = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = ct };
            http.Request.Method = "GET";
            http.Request.Headers.Authorization = "Basic test";
            foreach (var (key, value) in values) { http.Request.RouteValues[key] = value; }
            http.Response.Body = new MemoryStream();
            http.SetEndpoint(endpoint);
            await endpoint.RequestDelegate!(http);
            return http;
        }
    }

    private sealed class PersonalBrainAccess : IBrainAccess
    {
        public ValueTask<bool> CanAccessAsync(string principalId, string brainId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(principalId == brainId);
    }

    [Fact]
    public void AppsCompositionMapsDraftsVerificationAndBrainScopedDiscussions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton<MarketplaceService>(_ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new AppsModule().Configure(app);
        var routes = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        Assert.Contains("/packages/drafts/{id}/build", routes);
        Assert.Contains("/packages/{owner}/{name}/verify", routes);
        Assert.Contains("/brains/{brainId}/packages/{owner}/{name}/invocations/{invocationId:guid}/discussion", routes);
    }

    [Fact]
    public void AppCatalogAndConsentAreServedOnlyUnderTheBrainRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IDigitalBrain), _ => null!);
        builder.Services.AddSingleton<MarketplaceService>(_ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new AppsModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Where(endpoint => endpoint.RoutePattern.RawText!.Contains("/apps/", StringComparison.Ordinal)).Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "GET /brains/{brainId}/apps/",
                "GET /brains/{brainId}/apps/{appId}/consent",
                "POST /brains/{brainId}/apps/{appId}/consent/approve",
            ],
            routes);
    }
}

