using System.Net;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Telegram;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleEndpointFacts
{
    [Fact]
    public async Task Runtime_reuses_the_configured_module_instance_for_endpoint_mapping()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(ProbeModule)]) });
        var module = Assert.IsType<ProbeModule>(Assert.Single(brain.SiloServices.GetServices<IModule>()));
        Assert.True(module.SiloConfigured);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IModule>(module);
        await using var app = builder.Build();
        app.MapModuleEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        Assert.Equal("configured", await client.GetStringAsync("/module", TestContext.Current.CancellationToken));
        Assert.True(module.EndpointsConfigured);
    }

    [Theory]
    [InlineData("GET", "/telegram/health", true, false, 200)]
    [InlineData("GET", "/telegram/health", false, false, 401)]
    [InlineData("POST", "/telegram/webhook", true, false, 200)]
    [InlineData("POST", "/telegram/webhook", false, true, 401)]
    [InlineData("GET", "/telegram/miniapp/state", false, true, 401)]
    [InlineData("GET", "/telegram/unknown", false, false, 401)]
    [InlineData("GET", "/telegram/health/extra", true, false, 401)]
    [InlineData("GET", "/agent/connections/telegram", false, false, 401)]
    [InlineData("GET", "/agent/connections/telegram", false, true, 200)]
    [InlineData("GET", "/owner", true, false, 401)]
    [InlineData("GET", "/owner", false, true, 200)]
    public async Task Only_selected_module_endpoints_bypass_owner_authentication(string method, string path, bool secret, bool owner, int expected)
    {
        await using var app = BuildApp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var request = Request(method, path, secret, owner);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/telegram/health", 200)]
    [InlineData("POST", "/telegram/webhook", 200)]
    [InlineData("POST", "/telegram/health", 404)]
    [InlineData("GET", "/telegram/unknown", 404)]
    [InlineData("GET", "/telegram/app/missing.js", 404)]
    [InlineData("GET", "/agent/connections/telegram", 404)]
    [InlineData("GET", "/owner", 404)]
    [InlineData("GET", "/health", 404)]
    [InlineData("GET", "/oauth/callback", 404)]
    public async Task Module_listener_rejects_other_routes_even_with_owner_credentials(string method, string path, int expected)
    {
        await using var app = BuildApp(publicListener: true);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var request = Request(method, path, secret: true, owner: true);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
    }

    [Fact]
    public async Task Only_existing_bundle_assets_are_exposed_without_owner_credentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "digitalbrain-module-assets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "miniapp", TestContext.Current.CancellationToken);
            await using var app = BuildApp(bundleRoot: root);
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var client = app.GetTestClient();
            Assert.Equal("miniapp", await client.GetStringAsync("/telegram/app", TestContext.Current.CancellationToken));
            Assert.Equal("miniapp", await client.GetStringAsync("/telegram/app/index.html", TestContext.Current.CancellationToken));
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/telegram/app/index.html");
            using var head = await client.SendAsync(headRequest, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            using var missing = await client.GetAsync("/telegram/app/missing.js", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    private static WebApplication BuildApp(bool publicListener = false, string bundleRoot = "")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [BasicAuthGate.UsernameConfigurationKey] = "owner",
            [BasicAuthGate.PasswordConfigurationKey] = "password",
        });
        builder.Services.AddSingleton<IModule, TelegramModule>();
        builder.Services.AddSingleton(new TelegramOptions { BotToken = "123:test", WebhookSecret = "secret", MiniAppRoot = bundleRoot });
        builder.Services.AddSingleton<TelegramConnectionStatus>();
        builder.Services.AddSingleton<TelegramHttpSurface>();
        builder.Services.AddSingleton(new ModuleEndpointListener(typeof(TelegramModule), 5081));
        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.LocalPort = publicListener ? 5081 : 5080;
            return next(context);
        });
        app.UseRouting();
        app.UseModuleEndpointIsolation();
        // Model a legacy middleware surface that could otherwise answer before the owner gate.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/oauth/callback") { await context.Response.WriteAsync("callback"); }
            else { await next(context); }
        });
        app.UseBasicAuthGate();
        app.MapModuleEndpoints();
        app.MapGet("/owner", () => "owner");
        app.MapGet("/health", () => "healthy");
        return app;
    }

    private static HttpRequestMessage Request(string method, string path, bool secret, bool owner)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") { request.Content = new StringContent("{}"); }
        if (secret) { request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "secret"); }
        if (owner) { request.Headers.Authorization = new("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("owner:password"))); }
        return request;
    }

    public sealed class ProbeModule : IModule
    {
        public bool SiloConfigured { get; private set; }
        public bool EndpointsConfigured { get; private set; }
        public void Configure(ISiloBuilder builder) => SiloConfigured = true;
        public void Configure(IEndpointRouteBuilder endpoints)
        {
            EndpointsConfigured = true;
            endpoints.MapGet("/module", () => SiloConfigured ? "configured" : "wrong instance");
        }
    }
}
