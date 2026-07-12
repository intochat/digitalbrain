extern alias McpProject;

using System.Security.Claims;
using DigitalBrain.Core.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RuntimeRequestAuthenticator = McpProject::DigitalBrain.Mcp.RuntimeRequestAuthenticator;
using RuntimeSessionAuthority = McpProject::DigitalBrain.Mcp.RuntimeSessionAuthority;
using McpAuthority = McpProject::DigitalBrain.Mcp.McpAuthority;
using UiExternalIdentityAuthenticator = McpProject::DigitalBrain.Mcp.UiExternalIdentityAuthenticator;
using UiExternalIdentityOptions = McpProject::DigitalBrain.Mcp.UiExternalIdentityOptions;

namespace DigitalBrain.Tests.Runtime;

public sealed class RuntimeRequestAuthenticatorTests
{
    [Fact]
    public void Mcp_tool_capability_is_required_and_never_synthesized()
    {
        var context = new DigitalBrain.Core.Runtime.RequestContext(
            new("tenant"),
            new("workspace"),
            new("principal", PrincipalKind.User),
            "session",
            AuthAssurance.Oidc,
            "correlation",
            "idempotency",
            new HashSet<string>(["brain.read"], StringComparer.Ordinal));

        Assert.Throws<UnauthorizedAccessException>(() =>
            McpAuthority.DemandGrant(context, "brain.interact"));
        McpAuthority.DemandGrant(
            context with { Grants = new HashSet<string>(["brain.interact"], StringComparer.Ordinal) },
            "brain.interact");
    }

    [Fact]
    public async Task Mcp_accepts_framework_validated_external_identity_with_exact_scope()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", "tenant"),
            new Claim("workspace_id", "workspace"),
            new Claim("sub", "principal"),
            new Claim("digitalbrain_grants", "brain.interact")
        ], "oidc"));
        var context = HttpContext(principal, "Bearer external.jwt.token");
        var result = await Authenticator().AuthenticateMcpAsync(context);

        Assert.NotNull(result);
        Assert.Equal("tenant", result.TenantId.Value);
        Assert.Equal("workspace", result.WorkspaceId.Value);
        Assert.Equal("principal", result.Principal.Value);
        Assert.Equal(AuthAssurance.Oidc, result.Assurance);
        Assert.Equal(["brain.interact"], result.Grants);
    }

    [Fact]
    public async Task Mcp_rejects_missing_malformed_and_unallowlisted_external_identity()
    {
        var validPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", "tenant"),
            new Claim("workspace_id", "workspace"),
            new Claim("sub", "principal"),
            new Claim("digitalbrain_grants", "brain.interact")
        ], "oidc"));
        var authenticator = Authenticator();

        Assert.Null(await authenticator.AuthenticateMcpAsync(HttpContext(validPrincipal, null)));
        Assert.Null(await authenticator.AuthenticateMcpAsync(HttpContext(validPrincipal, "Bearer  padded ")));

        var elevated = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("tenant_id", "tenant"),
            new Claim("workspace_id", "workspace"),
            new Claim("sub", "principal"),
            new Claim("digitalbrain_grants", "brain.admin")
        ], "oidc"));
        Assert.Null(await authenticator.AuthenticateMcpAsync(
            HttpContext(elevated, "Bearer external.jwt.token")));
    }

    private static RuntimeRequestAuthenticator Authenticator()
    {
        var options = new UiExternalIdentityOptions(
            true,
            "https://issuer.example/tenant",
            "digitalbrain-runtime",
            "tenant_id",
            "workspace_id",
            "sub",
            "digitalbrain_grants",
            new HashSet<string>(["brain.interact"], StringComparer.Ordinal),
            true);
        var sessions = new RuntimeSessionAuthority(
            null!,
            new SessionTokenService(Enumerable.Repeat((byte)7, 32).ToArray(), TimeProvider.System),
            TimeProvider.System);
        return new RuntimeRequestAuthenticator(
            sessions,
            new UiExternalIdentityAuthenticator(options));
    }

    private static DefaultHttpContext HttpContext(ClaimsPrincipal principal, string? authorization)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(new FixedAuthenticationService(principal))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        return context;
    }

    private sealed class FixedAuthenticationService(ClaimsPrincipal principal) : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme!)));

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;
    }
}
