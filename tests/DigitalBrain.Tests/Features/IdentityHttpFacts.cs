using System.Net;
using System.Net.Http.Json;
using System.Text;
using DigitalBrain.Core;
using DigitalBrain.Identity;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class IdentityHttpFacts
{
    [Fact]
    public async Task Public_workspace_requires_owner_session_and_private_workspace_keeps_basic_gate()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var identities = brain.SiloServices.GetRequiredService<IdentityService>();
        var owner = await identities.BootstrapOwnerAsync(new("owner", "owner"), "owner", TimeSpan.FromHours(1));
        await identities.CreateMemberAsync(owner.Token, new("test", "member"), "owner");
        var member = (await identities.SignInAsync(new("test", "member"), TimeSpan.FromHours(1)))!;
        foreach (var publicListener in new[] { true, false })
        {
            await using var app = Build(brain, configuredOwner: true, publicListener: publicListener);
            app.MapOwnerWorkspace().MapGet("/workspace-test", () => Results.Ok());
            await app.StartAsync(TestContext.Current.CancellationToken);
            using var client = app.GetTestClient();
            using var anonymous = await client.GetAsync("/workspace-test", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("owner:password")));
            using var basic = await client.GetAsync("/workspace-test", TestContext.Current.CancellationToken);
            Assert.Equal(publicListener ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, basic.StatusCode);
            if (!publicListener)
            {
                client.DefaultRequestHeaders.Add("X-DigitalBrain-Session-Transport", "bearer");
                using var bootstrap = await client.PostAsync("/identity/owner/bootstrap", null, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
                Assert.False(bootstrap.Headers.Contains("Set-Cookie"));
            }
            client.DefaultRequestHeaders.Authorization = new("Bearer", member.Token);
            using var forbidden = await client.GetAsync("/workspace-test", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", owner.Token);
            using var accepted = await client.GetAsync("/workspace-test", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
    }
    [Fact]
    public async Task Bootstrap_link_login_cookie_csrf_and_revocation_require_verified_credentials()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var app = Build(brain, configuredOwner: true);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var anonymous = await client.PostAsync("/identity/owner/bootstrap", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var unlinked = await client.PostAsJsonAsync("/identity/login/test", new { credential = "verified" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unlinked.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("owner:password")));
        using var bootstrap = await client.PostAsync("/identity/owner/bootstrap", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var owner = (await bootstrap.Content.ReadFromJsonAsync<IdentitySession>(TestContext.Current.CancellationToken))!;
        var identities = brain.SiloServices.GetRequiredService<IdentityService>();
        await identities.CreateMemberAsync(owner.Token, new("test", "member"), "owner");
        var member = (await identities.SignInAsync(new("test", "member"), TimeSpan.FromHours(1)))!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", member.Token);
        using var denied = await client.GetAsync("/owner", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var cookie = Assert.Single(bootstrap.Headers.GetValues("Set-Cookie"));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        client.DefaultRequestHeaders.Authorization = new("Bearer", owner.Token);
        using var linkResponse = await client.PostAsync("/identity/link", null, TestContext.Current.CancellationToken);
        linkResponse.EnsureSuccessStatusCode();
        var code = (await linkResponse.Content.ReadFromJsonAsync<LinkCode>(TestContext.Current.CancellationToken))!.Code;
        client.DefaultRequestHeaders.Authorization = null;
        using var linked = await client.PostAsJsonAsync("/identity/link/redeem/test", new { credential = "verified", code }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        using var reused = await client.PostAsJsonAsync("/identity/link/redeem/test", new { credential = "verified", code }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        using var login = await client.PostAsJsonAsync("/identity/login/test", new { credential = "verified" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        client.DefaultRequestHeaders.Add("Cookie", IdentityAuthentication.CookieName + "=" + owner.Token);
        using var noCsrf = await client.PostAsync("/identity/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, noCsrf.StatusCode);
        client.DefaultRequestHeaders.Add(IdentityAuthentication.CsrfHeader, owner.CsrfToken);
        using var logout = await client.PostAsync("/identity/logout", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await client.GetAsync("/identity/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Local_development_cannot_bootstrap_and_invalid_bearer_cannot_downgrade()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var app = Build(brain, configuredOwner: false);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        using var bootstrap = await client.PostAsync("/identity/owner/bootstrap", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, bootstrap.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "invalid");
        using var owner = await client.GetAsync("/owner", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, owner.StatusCode);
    }

    [Fact]
    public async Task Public_listener_exposes_identity_but_never_legacy_or_unknown_routes()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        await using var app = Build(brain, configuredOwner: true, publicListener: true);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("owner:password")));
        using var bootstrap = await client.PostAsync("/identity/owner/bootstrap", null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, bootstrap.StatusCode);
        var owner = await brain.SiloServices.GetRequiredService<IdentityService>()
            .BootstrapOwnerAsync(new("owner", "owner"), "owner", TimeSpan.FromHours(1));
        client.DefaultRequestHeaders.Authorization = new("Bearer", owner.Token);
        using var session = await client.GetAsync("/identity/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        foreach (var path in new[] { "/owner", "/identity/missing", "/health" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static WebApplication Build(BrainSimulation brain, bool configuredOwner, bool publicListener = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        if (configuredOwner)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BasicAuthGate.UsernameConfigurationKey] = "owner",
                [BasicAuthGate.PasswordConfigurationKey] = "password"
            });
        }
        builder.Services.AddSingleton(brain.Grains);
        IdentityModule.AddServices(builder.Services);
        builder.Services.AddWorkspaceEndpointAccess();
        builder.Services.AddSingleton<IModule, IdentityModule>();
        builder.Services.AddSingleton<IExternalIdentityVerifier, TestVerifier>();
        builder.Services.AddSingleton(new ModuleEndpointListener(typeof(IdentityModule), 5081)
        {
            AdditionalModuleTypes = [typeof(DigitalBrain.UI.UIModule)],
        });
        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.LocalPort = publicListener ? 5081 : 5080;
            return next(context);
        });
        app.UseRouting();
        app.UseModuleEndpointIsolation();
        app.UseAuthentication();
        app.UseIdentityCallerContext();
        app.UseAuthorization();
        app.UseBasicAuthGate();
        app.MapModuleEndpoints();
        app.MapGet("/owner", () => Results.Ok());
        return app;
    }

    private sealed record LinkCode(string Code);
    private sealed class TestVerifier : IExternalIdentityVerifier
    {
        public string Provider => "test";
        public ValueTask<VerifiedExternalIdentity?> VerifyAsync(string credential, CancellationToken cancellationToken)
            => ValueTask.FromResult<VerifiedExternalIdentity?>(credential == "verified" ? new("test", "subject") : null);
    }
}


