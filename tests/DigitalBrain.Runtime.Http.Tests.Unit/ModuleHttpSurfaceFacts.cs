using System.Net;
using System.Security.Claims;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Testing.Conformance.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Runtime.Http.Tests.Unit;

// The 26 auth routes of 09-WAVE0-CHECKS.md row 10, as the auth module will map them: A = anonymous.
internal sealed class AuthSurface : IModuleHttpSurface
{
    public static readonly (string Method, string Path, bool Anonymous)[] Routes =
    [
        ("POST", "/auth/signup", true), ("POST", "/auth/codes", true), ("POST", "/auth/codes/{codeId}/verify", true),
        ("POST", "/auth/login", true), ("POST", "/auth/login/challenges/{challengeId}/verify", true),
        ("POST", "/auth/login/challenges/{challengeId}/resend", true), ("POST", "/auth/password-reset", true),
        ("POST", "/auth/password-reset/complete", true), ("GET", "/auth/google/start", true), ("GET", "/auth/google/complete", true),
        ("GET", "/auth/google/callback", true), ("POST", "/auth/totp", false), ("POST", "/auth/totp/confirm", false),
        ("POST", "/auth/totp/disable", false), ("POST", "/auth/totp/recovery-codes", false), ("GET", "/auth/profile", false),
        ("PUT", "/auth/profile", false), ("GET", "/auth/avatar", false), ("PUT", "/auth/avatar", false), ("DELETE", "/auth/avatar", false),
        ("POST", "/auth/email-change", false), ("POST", "/auth/email-change/confirm", false), ("GET", "/auth/sessions", false),
        ("DELETE", "/auth/sessions/{handle}", false), ("POST", "/auth/sessions/revoke-others", false), ("GET", "/auth/license", false),
    ];

    public void Map(IEndpointRouteBuilder brainGroup)
    {
        foreach (var (method, path, anonymous) in Routes)
        {
            var route = brainGroup.MapMethods(path, [method], () => Results.Ok(path));
            if (anonymous) { route.AllowAnonymous(); }
        }
    }
}

internal sealed class NotesSurface : IModuleHttpSurface
{
    public void Map(IEndpointRouteBuilder brainGroup)
    {
        BrainRouteGroups.Brain(brainGroup).MapGet("/notes", (HttpContext http) => Results.Ok(BrainRouteGroups.MemberOf(http)?.PrincipalId));
        BrainRouteGroups.Session(brainGroup, "/sandbox").MapGet("/ping", () => Results.Ok("pong")).AllowAnonymous();
    }
}

public sealed class ModuleHttpSurfaceFacts
{
    private const string TestPrincipalHeader = "X-Test-Principal";

    [Fact]
    public async Task Maps_the_26_auth_routes_with_explicit_anonymous_metadata()
    {
        await using var host = await Host.Start(services => services.AddModuleHttpSurface<AuthSurface>().AddModuleHttpSurface<AuthSurface>());
        var endpoints = host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        Assert.Equal(26, endpoints.Count);
        foreach (var (method, path, anonymous) in AuthSurface.Routes)
        {
            var endpoint = Assert.Single(endpoints, candidate => candidate.RoutePattern.RawText == path
                && candidate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
            Assert.Equal(anonymous, endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null);
        }
    }

    [Fact]
    public async Task Session_routes_need_an_authenticated_caller_and_anonymous_routes_do_not()
    {
        await using var host = await Host.Start(services => services.AddModuleHttpSurface<AuthSurface>());
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(HttpStatusCode.OK, (await host.Send(HttpMethod.Post, "/auth/signup", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(HttpMethod.Get, "/auth/google/callback", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Send(HttpMethod.Get, "/auth/profile", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Send(HttpMethod.Delete, "/auth/sessions/h1", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(HttpMethod.Get, "/auth/profile", "ada", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(HttpMethod.Delete, "/auth/sessions/h1", "ada", ct)).StatusCode);
    }

    [Fact]
    public async Task Brain_group_checks_the_id_and_the_membership()
    {
        var identity = new InMemoryIdentity();
        var ct = TestContext.Current.CancellationToken;
        var ada = (await identity.RegisterPrincipal("ada", "Ada", ct)).Member;
        await identity.RegisterPrincipal("bob", "Bob", ct);
        await using var host = await Host.Start(services => services
            .AddKeyedSingleton<IIdentity>(Capability.Identity.V1Id, identity)
            .AddModuleHttpSurface<NotesSurface>());
        var own = await host.Send(HttpMethod.Get, $"/brains/{ada.BrainId}/notes", "ada", ct);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("\"ada\"", await own.Content.ReadAsStringAsync(ct));
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Send(HttpMethod.Get, $"/brains/{ada.BrainId}/notes", "bob", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Send(HttpMethod.Get, $"/brains/{ada.BrainId}/notes", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Send(HttpMethod.Get, "/brains/%40kernel/notes", "ada", ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Send(HttpMethod.Get, $"/brains/{ada.BrainId}/sandbox/ping", null, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Send(HttpMethod.Get, "/brains/%40kernel/sandbox/ping", null, ct)).StatusCode);
    }

    [Fact]
    public async Task Brain_group_without_identity_is_forbidden()
    {
        await using var host = await Host.Start(services => services.AddModuleHttpSurface<NotesSurface>());
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Send(HttpMethod.Get, "/brains/brain-ada/notes", "ada", TestContext.Current.CancellationToken)).StatusCode);
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;

        public static async Task<Host> Start(Action<IServiceCollection> configure)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            configure(builder.Services);
            var app = builder.Build();
            // Stands in for the host authentication: the header names the signed-in principal.
            app.Use((http, next) =>
            {
                if (http.Request.Headers[TestPrincipalHeader].ToString() is { Length: > 0 } principal)
                {
                    http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaims.Principal, principal)], "test"));
                }

                return next(http);
            });
            app.MapModuleHttpSurfaces();
            await app.StartAsync(TestContext.Current.CancellationToken);
            return new Host(app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
        }

        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? principal, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, path);
            if (principal is not null) { request.Headers.Add(TestPrincipalHeader, principal); }
            return await client.SendAsync(request, ct);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}

public sealed class SessionPrincipalFacts
{
    private static readonly Member Ada = new() { PrincipalId = "ada", AccountId = "acc", BrainId = "brain-ada", Role = MemberRole.Owner, DisplayName = "Ada" };

    [Fact]
    public void Owned_claims_come_from_the_member_and_extra_claims_are_kept()
    {
        var request = new SessionRequest { Member = Ada, Claims = new Dictionary<string, string> { ["auth_time"] = "1", ["amr"] = "pwd" } };
        var principal = SessionPrincipal.Create(request, new SessionIssued("s1", DateTimeOffset.UnixEpoch), "cookie");
        Assert.Equal("ada", principal.FindFirst(SessionClaims.Principal)?.Value);
        Assert.Equal("acc", principal.FindFirst(SessionClaims.Account)?.Value);
        Assert.Equal("brain-ada", principal.FindFirst(SessionClaims.Brain)?.Value);
        Assert.Equal("Owner", principal.FindFirst(SessionClaims.Role)?.Value);
        Assert.Equal("pwd", principal.FindFirst("amr")?.Value);
        Assert.Equal("s1", SessionPrincipal.SessionId(principal));
        Assert.Equal("ada", SessionPrincipal.PrincipalId(principal));
        Assert.Single(principal.FindAll(SessionClaims.Brain));
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("account")]
    [InlineData("brain")]
    [InlineData("role")]
    public void Owned_claim_in_the_request_is_rejected(string claim)
    {
        var request = new SessionRequest { Member = Ada, Claims = new Dictionary<string, string> { [claim] = "mallory" } };
        Assert.Throws<ArgumentException>(() => SessionPrincipal.Create(request, new SessionIssued("s1", DateTimeOffset.UnixEpoch), "cookie"));
    }

    [Fact]
    public void Unauthenticated_user_has_no_principal()
    {
        Assert.Null(SessionPrincipal.PrincipalId(new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionClaims.Principal, "ada")]))));
        Assert.Null(SessionPrincipal.PrincipalId(null));
    }
}
