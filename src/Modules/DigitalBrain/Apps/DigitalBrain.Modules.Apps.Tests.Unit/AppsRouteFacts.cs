using DigitalBrain.Apps;
using DigitalBrain;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Testing.Unit;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using System.Text.Json;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AppsRouteFacts
{
    [Fact]
    public async Task AHostWithoutABuiltInSettingsAppReportsItAsUnavailable()
    {
        var result = await BuiltInSettingsEndpoints.OpenSettings(null!, null!, new BuiltInSettingsOptions());
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
    }
    [Theory]
    [InlineData("missing", 404)]
    [InlineData("no-open", 422)]
    [InlineData("foreign", 403)]
    [InlineData("happy", 200)]
    [InlineData("failed", 422)]
    [InlineData("invalid-json", 422)]
    [InlineData("invoke-text", 200)]
    public async Task OpenUsesTheInstalledRevisionAndReturnsItsInvocationResult(string scenario, int expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<AppsModule>().StartAsync(ct);
        CallerContextStamper.Stamp(new CallerContext
        {
            PrincipalId = "alice",
            AccountId = "alice",
            BrainId = "alice",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
        });
        var id = PackageId.Create("alice", "customer-researcher");
        var operation = scenario == "invoke-text" ? "research" : "open";
        var package = brain.Get<IPackage>(id.ToString());
        var content = new PackageContent(new("Customer Researcher", "Research companies", [new("ask", "Ask")], []), "// installed behavior");
        var published = await package.Commit(new(Guid.NewGuid(), null, content, "Initial"));
        await package.Publish(new(Guid.NewGuid(), published.Id));
        // The installed revision, rather than the marketplace's version, declares open.
        var revision = scenario == "no-open" ? published : await package.Commit(new(Guid.NewGuid(), published.Id,
            content with { Manifest = content.Manifest with { Operations = [new(operation, "Run")] } }, "Add operation"));
        var installed = brain.Get<IApp>(BrainScope.Create("alice", "alice").Id + "/packages/" + id);
        if (scenario != "missing") { await installed.Install(new(Guid.NewGuid(), new(id, revision.Id), new Dictionary<string, string>())); }

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IDigitalBrain>(brain.SiloServices.GetRequiredService<IDigitalBrain>());
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<MarketplaceService>());
        builder.Services.AddSingleton<IBrainAccess, PersonalBrainAccess>();
        await using var app = builder.Build();
        new AppsModule().Configure(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == (scenario == "invoke-text"
                ? "/brains/{brainId}/apps/{appId}/invoke/{operation}" : "/brains/{brainId}/apps/{appId}/open"));
        var http = new DefaultHttpContext { RequestServices = app.Services, RequestAborted = ct };
        http.Request.Method = "POST";
        http.Request.Headers.Authorization = "Basic test";
        http.Request.RouteValues["brainId"] = scenario == "foreign" ? "bob" : "alice";
        http.Request.RouteValues["appId"] = "customer-researcher";
        http.Request.RouteValues["operation"] = operation;
        http.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"query\":\"test\"}"));
        http.Response.Body = new MemoryStream();
        http.SetEndpoint(endpoint);

        var answer = scenario is "happy" or "failed" or "invalid-json" or "invoke-text" ? Answer() : Task.CompletedTask;
        await endpoint.RequestDelegate!(http);
        await answer;
        Assert.Equal(expected, http.Response.StatusCode);
        if (scenario == "foreign") { return; }
        http.Response.Body.Position = 0;
        if (scenario == "invoke-text")
        {
            Assert.StartsWith("text/plain", http.Response.ContentType);
            Assert.Equal("Research result", await new StreamReader(http.Response.Body).ReadToEndAsync(ct));
            return;
        }
        using var response = await JsonDocument.ParseAsync(http.Response.Body, cancellationToken: ct);
        if (scenario == "happy")
        {
            Assert.Equal("research-window", response.RootElement.GetProperty("id").GetString());
            Assert.Equal("Customer Researcher", response.RootElement.GetProperty("title").GetString());
            Assert.Equal("surface", response.RootElement.GetProperty("surface").GetProperty("kind").GetString());
        }
        if (scenario == "missing") { Assert.Contains("not installed", response.RootElement.GetProperty("error").GetString()); }
        if (scenario == "no-open") { Assert.Contains("no 'open'", response.RootElement.GetProperty("error").GetString()); }
        if (scenario == "failed") { Assert.Equal("Cannot open", response.RootElement.GetProperty("error").GetString()); }

        async Task Answer()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            AppInvocation? invocation = null;
            while (invocation is null)
            {
                invocation = (await installed.Pending().WaitAsync(deadline.Token)).SingleOrDefault();
                if (invocation is null) { await Task.Delay(10, deadline.Token); }
            }
            Assert.Equal(operation, invocation.Operation);
            Assert.Equal("{\"query\":\"test\"}", invocation.Input);
            await installed.Respond(new(invocation.Id,
                scenario == "invoke-text" ? "Research result" : scenario == "invalid-json" ? "invalid" : "{\"id\":\"research-window\",\"title\":\"Customer Researcher\",\"surface\":{\"kind\":\"surface\",\"name\":\"research/surface\"}}",
                scenario == "failed" ? "Cannot open" : null));
        }
    }

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
            CallerContextStamper.Stamp(new CallerContext
            {
                PrincipalId = principal,
                AccountId = principal,
                BrainId = principal,
                Kind = CallerKind.User,
                StampedBy = TrustedEdge.AuthenticatedHttp
            });
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
    public void InstalledAppsAndInvocationsAreServedOnlyUnderTheBrainRoute()
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
                "POST /brains/{brainId}/apps/{appId}/invoke/{operation}",
                "POST /brains/{brainId}/apps/{appId}/open",
            ],
            routes);
    }
}

