using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using IPAddress = System.Net.IPAddress;

namespace DigitalBrain.Tests;

public sealed class AuthEndpointProofs
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AnonymousCallerGets401OnProtectedSurface()
    {
        await using var host = await StartAuthHostAsync(allowLoopbackDev: false, environmentName: Environments.Production);
        var client = CreateClient(host);

        var response = await client.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BootstrapThenLoginRoundTripReturnsMeAndAuthorizesProtectedSurface()
    {
        await using var host = await StartAuthHostAsync(allowLoopbackDev: false, environmentName: Environments.Production);
        var client = CreateClient(host);
        var workspace = host.Services.GetRequiredService<RecordingWorkspaceGateway>();

        var bootstrap = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        var me = await bootstrap.Content.ReadFromJsonAsync<AuthMeDto>(Json, TestContext.Current.CancellationToken);
        Assert.NotNull(me);
        Assert.Equal("owner", me.Username);
        Assert.True(me.IsBootstrapOwner);
        Assert.Single(workspace.Added);
        Assert.Equal(WorkspaceRole.Owner, workspace.Added[0].Role);

        var logout = await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var anonymous = await client.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/auth/login",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        ForwardAuthCookie(client, login);

        var authorized = await client.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        var meAgain = await client.GetFromJsonAsync<AuthMeDto>("/auth/me", Json, TestContext.Current.CancellationToken);
        Assert.NotNull(meAgain);
        Assert.Equal(me.PrincipalId, meAgain.PrincipalId);
    }

    [Fact]
    public async Task BootstrapIsRefusedOnceAnOwnerExists()
    {
        await using var host = await StartAuthHostAsync(allowLoopbackDev: false, environmentName: Environments.Production);
        var client = CreateClient(host);

        var first = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        var second = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "other", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task LoopbackDevBypassIsActiveOnlyInDevelopmentWhenEnabled()
    {
        await using var devHost = await StartAuthHostAsync(
            allowLoopbackDev: true,
            environmentName: Environments.Development,
            seedBootstrapOwner: true);
        var devClient = CreateClient(devHost);
        var dev = await devClient.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, dev.StatusCode);

        await using var prodHost = await StartAuthHostAsync(
            allowLoopbackDev: true,
            environmentName: Environments.Production,
            seedBootstrapOwner: true);
        var prodClient = CreateClient(prodHost);
        var prod = await prodClient.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, prod.StatusCode);
    }

    [Fact]
    public async Task TwoUsersReceiveDistinctPrincipalScopedChatAndSurfaceInstanceNames()
    {
        await using var host = await StartAuthHostAsync(
            allowLoopbackDev: false,
            environmentName: Environments.Production,
            mapRealCommands: true);
        var client = CreateClient(host);
        var workspace = host.Services.GetRequiredService<RecordingWorkspaceGateway>();

        var ownerBootstrap = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        var ownerMe = await ownerBootstrap.Content.ReadFromJsonAsync<AuthMeDto>(Json, TestContext.Current.CancellationToken);
        Assert.NotNull(ownerMe);
        ForwardAuthCookie(client, ownerBootstrap);

        var create = await client.PostAsJsonAsync(
            "/auth/users",
            new { username = "friend", password = "password1", role = "Builder" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var friendMe = await create.Content.ReadFromJsonAsync<AuthMeDto>(Json, TestContext.Current.CancellationToken);
        Assert.NotNull(friendMe);
        Assert.NotEqual(ownerMe.PrincipalId, friendMe.PrincipalId);
        Assert.Equal(2, workspace.Added.Count);

        var ownerPrincipal = new PrincipalId(Guid.Parse(ownerMe.PrincipalId));
        var friendPrincipal = new PrincipalId(Guid.Parse(friendMe.PrincipalId));
        Assert.NotEqual(
            PrincipalChat.InstanceName(ownerPrincipal, "main"),
            PrincipalChat.InstanceName(friendPrincipal, "main"));
        Assert.NotEqual(
            PrincipalSurface.InstanceName(ownerPrincipal, "desk"),
            PrincipalSurface.InstanceName(friendPrincipal, "desk"));

        // Real surface.open command path: recorded instance is principal-scoped, never raw client name.
        var brain = host.Services.GetRequiredService<RecordingBrain>();
        var open = await client.PostAsJsonAsync(
            "/owner/commands",
            new
            {
                kind = "surface.open",
                surfaceName = "desk",
                surfaceKey = "main",
                title = "Desk",
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, open.StatusCode);
        Assert.Single(brain.SurfaceOpens);
        Assert.Equal(PrincipalSurface.InstanceName(ownerPrincipal, "desk"), brain.SurfaceOpens[0].InstanceName);
        Assert.NotEqual("desk", brain.SurfaceOpens[0].InstanceName);
    }

    [Fact]
    public async Task NonLoopbackPlainHttpIsRefusedWith403()
    {
        await using var host = await StartAuthHostAsync(allowLoopbackDev: false, environmentName: Environments.Production);
        var client = CreateClient(host);
        client.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.10");

        var response = await client.GetAsync(new Uri("/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("HTTPS is required beyond localhost", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonLoopbackWithForwardedHttpsIsAllowedPastHttpsStance()
    {
        await using var host = await StartAuthHostAsync(allowLoopbackDev: false, environmentName: Environments.Production);
        var client = CreateClient(host);
        client.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "203.0.113.10");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        // Past HTTPS stance: anonymous still hits auth (401), not transport refusal (403).
        var response = await client.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoopbackPlainHttpRemainsAllowedByHttpsStance()
    {
        await using var host = await StartAuthHostAsync(allowLoopbackDev: false, environmentName: Environments.Production);
        var client = CreateClient(host);
        // Default test remote IP is loopback; plain HTTP must pass stance (then auth applies).
        var response = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginRehashesPasswordWhenVerificationRequestsRehash()
    {
        await using var host = await StartAuthHostAsync(
            allowLoopbackDev: false,
            environmentName: Environments.Production,
            rehashOnVerify: true);
        var client = CreateClient(host);
        var accounts = host.Services.GetRequiredService<MemoryAccountDirectory>();
        var hasher = host.Services.GetRequiredService<IPasswordHasher<DigitalBrainUser>>();

        var bootstrap = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        await client.PostAsync(new Uri("/auth/logout", UriKind.Relative), content: null, TestContext.Current.CancellationToken);

        // Downgrade stored hash to the legacy form so Verify returns SuccessRehashNeeded.
        var before = await accounts.FindByNameAsync("OWNER", CancellationToken.None);
        Assert.NotNull(before);
        before.PasswordHash = "v1:password1";
        await accounts.UpdateAsync(before, CancellationToken.None);

        var login = await client.PostAsJsonAsync(
            "/auth/login",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var after = await accounts.FindByNameAsync("OWNER", CancellationToken.None);
        Assert.NotNull(after);
        Assert.NotEqual("v1:password1", after.PasswordHash);
        Assert.Equal("v2:password1", after.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(after, after.PasswordHash!, "password1"));
    }

    [Fact]
    public async Task NullRemoteIpIsNotTreatedAsLoopback()
    {
        await using var host = await StartAuthHostAsync(
            allowLoopbackDev: true,
            environmentName: Environments.Development,
            seedBootstrapOwner: true);
        var client = CreateClient(host);
        client.DefaultRequestHeaders.Add("X-Test-Remote-Ip", "none");

        // Null remote is remote for both HTTPS stance and loopback dev bypass.
        var response = await client.GetAsync(new Uri("/owner/commands", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MalformedConversationNameOnChatCommandReturns400()
    {
        await using var host = await StartAuthHostAsync(
            allowLoopbackDev: false,
            environmentName: Environments.Production,
            mapRealCommands: true);
        var client = CreateClient(host);

        var bootstrap = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        ForwardAuthCookie(client, bootstrap);

        var response = await client.PostAsJsonAsync(
            "/owner/commands",
            new { kind = "chat.send", chatName = "bad/name", text = "hello" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MalformedSurfaceNameOnSurfaceOpenReturns400()
    {
        await using var host = await StartAuthHostAsync(
            allowLoopbackDev: false,
            environmentName: Environments.Production,
            mapRealCommands: true);
        var client = CreateClient(host);

        var bootstrap = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        ForwardAuthCookie(client, bootstrap);

        var response = await client.PostAsJsonAsync(
            "/owner/commands",
            new { kind = "surface.open", surfaceName = "desk top", surfaceKey = "k", title = "T" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BrokenAuthClaimsOnCommandEndpointReturn401Not500()
    {
        await using var host = await StartAuthHostAsync(
            allowLoopbackDev: false,
            environmentName: Environments.Production,
            mapRealCommands: true,
            injectBrokenClaimsProbe: true);
        var client = CreateClient(host);

        // Sign in so authorization passes, then overwrite claims (missing PrincipalId) → endpoint 401, not 500.
        var bootstrap = await client.PostAsJsonAsync(
            "/auth/bootstrap",
            new { username = "owner", password = "password1" },
            TestContext.Current.CancellationToken);
        ForwardAuthCookie(client, bootstrap);
        client.DefaultRequestHeaders.Add("X-Test-Broken-Claims", "1");
        var response = await client.PostAsJsonAsync(
            "/owner/commands",
            new { kind = "chat.send", chatName = "main", text = "hello" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void HttpActorTryGetRejectsBrokenClaimsWithoutThrowing()
    {
        var http = new DefaultHttpContext();
        var identity = new ClaimsIdentity(authenticationType: AuthHostingExtensions.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, "broken"));
        http.User = new ClaimsPrincipal(identity);

        Assert.False(HttpActor.TryGet(http, out _));
    }

    [Fact]
    public void PrincipalScopedRejectsSlashAndWhitespace()
    {
        var principal = new PrincipalId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        Assert.Throws<ArgumentException>(() => PrincipalScoped.InstanceName(principal, "a/b"));
        Assert.Throws<ArgumentException>(() => PrincipalScoped.InstanceName(principal, "a b"));
        Assert.Equal(
            "aaaaaaaabbbbccccddddeeeeeeeeeeee.desk",
            PrincipalSurface.InstanceName(principal, "desk"));
    }

    private static async Task<WebApplication> StartAuthHostAsync(
        bool allowLoopbackDev,
        string environmentName,
        bool seedBootstrapOwner = false,
        bool rehashOnVerify = false,
        bool mapRealCommands = false,
        bool injectBrokenClaimsProbe = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
        });
        builder.WebHost.UseTestServer();
        builder.Configuration[AuthOptions.AllowLoopbackDevKey] = allowLoopbackDev ? "true" : "false";

        var accounts = new MemoryAccountDirectory();
        var workspace = new RecordingWorkspaceGateway();
        var brain = new RecordingBrain();

        builder.Services.AddSingleton(accounts);
        builder.Services.AddSingleton<IAccountDirectory>(accounts);
        builder.Services.AddSingleton(workspace);
        builder.Services.AddSingleton<IWorkspaceMembershipGateway>(workspace);
        builder.Services.AddSingleton(brain);
        builder.Services.AddSingleton<IDigitalBrain>(brain);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDataProtection();
        builder.AddDigitalBrainAuth();
        // Auth hosting TryAdds table directory — force memory for endpoint proofs.
        builder.Services.RemoveAll<IAccountDirectory>();
        builder.Services.AddSingleton<IAccountDirectory>(accounts);
        builder.Services.RemoveAll<IWorkspaceMembershipGateway>();
        builder.Services.AddSingleton<IWorkspaceMembershipGateway>(workspace);

        if (rehashOnVerify)
        {
            builder.Services.RemoveAll<IPasswordHasher<DigitalBrainUser>>();
            builder.Services.AddSingleton<IPasswordHasher<DigitalBrainUser>, RehashingPasswordHasher>();
        }

        var app = builder.Build();

        // TestServer leaves RemoteIpAddress unset; production Kestrel always sets it.
        // Default test traffic to loopback unless a test overrides via X-Test-Remote-Ip.
        app.Use(static async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var raw))
            {
                var text = raw.ToString();
                if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
                {
                    context.Connection.RemoteIpAddress = null;
                }
                else if (IPAddress.TryParse(text, out var parsed))
                {
                    context.Connection.RemoteIpAddress = parsed;
                }
            }
            else if (context.Connection.RemoteIpAddress is null)
            {
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
            }

            await next().ConfigureAwait(false);
        });

        app.UseDigitalBrainAuth();

        // After auth middleware: keep IsAuthenticated true so authorization passes, but strip PrincipalId
        // so MapOwnerCommands HttpActor.TryGet returns 401 (not an unhandled 500).
        if (injectBrokenClaimsProbe)
        {
            app.Use(static async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("X-Test-Broken-Claims")
                    && context.User.Identity?.IsAuthenticated == true)
                {
                    var identity = new ClaimsIdentity(authenticationType: AuthHostingExtensions.AuthenticationScheme);
                    identity.AddClaim(new Claim(ClaimTypes.Name, "broken"));
                    context.User = new ClaimsPrincipal(identity);
                }

                await next().ConfigureAwait(false);
            });
        }

        app.MapAuth();
        if (mapRealCommands)
        {
            app.MapOwnerCommands();
        }
        else
        {
            app.MapPost(
                "/owner/commands",
                static async Task (HttpContext http, OwnerCommandRequest request, RecordingBrain brain) =>
                {
                    if (!HttpActor.TryGet(http, out var actor))
                    {
                        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }

                    if (string.Equals(request.Kind, HttpSurfacePaths.KindSurfaceOpen, StringComparison.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(request.SurfaceName)
                            || string.IsNullOrWhiteSpace(request.SurfaceKey)
                            || string.IsNullOrWhiteSpace(request.Title))
                        {
                            http.Response.StatusCode = StatusCodes.Status400BadRequest;
                            return;
                        }

                        try
                        {
                            var instance = PrincipalSurface.InstanceName(actor.PrincipalId, request.SurfaceName);
                            brain.RecordSurfaceOpen(instance, request.SurfaceKey!, request.Title!);
                            http.Response.StatusCode = StatusCodes.Status202Accepted;
                        }
                        catch (ArgumentException)
                        {
                            http.Response.StatusCode = StatusCodes.Status400BadRequest;
                        }

                        return;
                    }

                    http.Response.StatusCode = StatusCodes.Status200OK;
                    await http.Response.WriteAsJsonAsync(new { ok = true }).ConfigureAwait(false);
                });
            app.MapGet("/owner/commands", () => Results.Ok(new { ok = true }));
        }

        app.MapGet("/oauth/callback", () => Results.Ok(new { ok = true })).AllowAnonymous();

        if (seedBootstrapOwner)
        {
            var principal = PrincipalId.New();
            await accounts.CreateAsync(new DigitalBrainUser
            {
                UserName = "owner",
                NormalizedUserName = "OWNER",
                PasswordHash = "unused",
                PrincipalId = principal.Value,
                IsBootstrapOwner = true,
                CreatedAt = DateTimeOffset.UtcNow,
            }, CancellationToken.None);
        }

        await app.StartAsync(CancellationToken.None);
        return app;
    }

    private static HttpClient CreateClient(WebApplication host)
    {
        var server = host.GetTestServer();
        // CookieContainerHandler alone does not always re-attach Identity cookies under TestServer;
        // tests forward Set-Cookie explicitly after sign-in (browsers do this natively).
        return new HttpClient(server.CreateHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        };
    }

    private static void ForwardAuthCookie(HttpClient client, HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var setCookies),
            "Authenticated responses must set DigitalBrain.Auth.");
        var sessionCookie = setCookies.First().Split(';', 2)[0];
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", sessionCookie);
    }

    private sealed class RecordingWorkspaceGateway : IWorkspaceMembershipGateway
    {
        public List<(ActorContext Actor, PrincipalId PrincipalId, string Username, WorkspaceRole Role)> Added { get; } = [];

        public List<WorkspaceMember> Members { get; } = [];

        public Task AddMemberAsync(
            ActorContext actor,
            PrincipalId principalId,
            string username,
            WorkspaceRole role,
            CancellationToken cancellationToken)
        {
            Added.Add((actor, principalId, username, role));
            Members.Add(new WorkspaceMember(principalId, username, role));
            return Task.CompletedTask;
        }

        public Task<Membership> ReadMembershipAsync(ActorContext actor, CancellationToken cancellationToken)
        {
            if (!Members.Any(member => member.PrincipalId == actor.PrincipalId))
            {
                throw new InvalidOperationException("not a member");
            }

            return Task.FromResult(new Membership(IWorkspace.InstanceName, Members));
        }
    }

    private sealed class RecordingBrain : IDigitalBrain
    {
        public List<(string InstanceName, string SurfaceKey, string Title)> SurfaceOpens { get; } = [];

        public OwnerId Owner { get; } = new("dev");

        public void RecordSurfaceOpen(string instanceName, string surfaceKey, string title)
            => SurfaceOpens.Add((instanceName, surfaceKey, title));

        public Task ActivateAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public NeuronReference<TNeuron> Get<TNeuron>(string name = "default")
            where TNeuron : INeuron
            => throw new NotSupportedException();

        public TNeuron GetGrainProxy<TNeuron>(string name = "default")
            where TNeuron : class, INeuron
            => throw new NotSupportedException();

        public Task FireAsync<TNeuron>(string name, Synapse synapse, CancellationToken cancellationToken = default)
            where TNeuron : INeuron
        {
            if (synapse is DigitalBrain.UI.OpenSurface open)
            {
                SurfaceOpens.Add((name, open.SurfaceKey, open.Title));
            }

            return Task.CompletedTask;
        }

        public Task FireAsync(NeuronId receiver, Synapse synapse, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task FireAsync(Synapse synapse, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<JournalRead> ReadJournalAsync(
            NeuronId subject,
            JournalKind kind,
            long afterSequence = 0,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<JournalRead> WatchJournalAsync(
            NeuronId subject,
            JournalKind kind,
            long afterSequence = 0,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RehashingPasswordHasher : IPasswordHasher<DigitalBrainUser>
    {
        public string HashPassword(DigitalBrainUser user, string password)
            => "v2:" + password;

        public PasswordVerificationResult VerifyHashedPassword(
            DigitalBrainUser user,
            string hashedPassword,
            string providedPassword)
        {
            if (hashedPassword == "v2:" + providedPassword)
            {
                return PasswordVerificationResult.Success;
            }

            if (hashedPassword == "v1:" + providedPassword)
            {
                return PasswordVerificationResult.SuccessRehashNeeded;
            }

            return PasswordVerificationResult.Failed;
        }
    }

    private sealed record AuthMeDto(string Username, string PrincipalId, bool IsBootstrapOwner);
}
