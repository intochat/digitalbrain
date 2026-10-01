using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Identity;
using DigitalBrain.Sdk.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Core.Tests.Unit.Identity;

public sealed class IdentityHostingFacts
{
    [Fact]
    public void TheHostProtectionIdentityOverridesTheWebApplicationsDefault()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCookieProtection();
        builder.Services.AddIdentity();
        builder.Services.Configure<IdentityHostOptions>(options => options.ProtectionApplicationName = "another-host.v1");
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Equal("another-host.v1", provider.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Fact]
    public async Task CookieChallengesAndForbidsReturnStatusesWithoutRedirects()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddIdentity();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UsePlatformHttp();
        app.MapGet("/challenge", async context => await context.ChallengeAsync());
        app.MapGet("/forbid", async context => await context.ForbidAsync());
        await app.StartAsync(ct);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(app.Urls.Single()) };
        using var challenge = await client.GetAsync("/challenge", ct);
        using var forbid = await client.GetAsync("/forbid", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbid.StatusCode);
        Assert.Null(challenge.Headers.Location);
        Assert.Null(forbid.Headers.Location);
        await app.StopAsync(ct);
    }

    [Fact]
    public async Task LoginUsesTheHostCookieAndTheAccountGateKeepsCorsOnUnauthorizedResponses()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["DigitalBrain:Auth:Username"] = "bootstrap";
        builder.Configuration["DigitalBrain:Auth:Password"] = "bootstrap-password";
        builder.Configuration["DigitalBrain:Cors:AllowedOrigin"] = "http://localhost:27880";
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddIdentity();
        builder.Services.Configure<IdentityHostOptions>(options => options.CookieName = "another-host.session");
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UsePlatformHttp();
        IdentityEndpoints.Map(app);
        await app.StartAsync(ct);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new(app.Urls.Single()) };
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:27880");
        using var denied = await client.GetAsync("/auth/check", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal("http://localhost:27880", Assert.Single(denied.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(denied.Headers.GetValues("Access-Control-Allow-Credentials")));
        client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("bootstrap:bootstrap-password")));
        using var basic = await client.GetAsync("/auth/check", ct);
        Assert.Equal(HttpStatusCode.NoContent, basic.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var principal = "login-" + Guid.NewGuid().ToString("N");
        using var registered = await client.PostAsJsonAsync("/identity/register", new { principalId = principal, password = "correct-password", displayName = "Reader" }, ct);
        registered.EnsureSuccessStatusCode();
        using var login = await client.PostAsJsonAsync("/identity/login", new { principalId = principal, password = "correct-password" }, ct);
        login.EnsureSuccessStatusCode();
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("another-host.session=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        using var authenticated = await client.GetAsync("/auth/check", ct);
        Assert.Equal(HttpStatusCode.NoContent, authenticated.StatusCode);
        using var session = await client.GetAsync("/identity/session", ct);
        using var payload = JsonDocument.Parse(await session.Content.ReadAsStringAsync(ct));
        Assert.Contains(principal, payload.RootElement.ToString(), StringComparison.Ordinal);
        await app.StopAsync(ct);
    }
}
