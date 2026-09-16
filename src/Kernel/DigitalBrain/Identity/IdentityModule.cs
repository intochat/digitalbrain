using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Identity;

public interface IExternalIdentityVerifier
{
    string Provider { get; }
    ValueTask<VerifiedExternalIdentity?> VerifyAsync(string credential, CancellationToken cancellationToken);
}

/// <summary>Built-in application identities. Provider integrations contribute proof verification only.</summary>
public sealed class IdentityModule : IModule
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddServices(builder.Services);
    }

    public static void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IdentityService>();
        services.AddAuthentication(IdentityAuthentication.Scheme)
            .AddScheme<AuthenticationSchemeOptions, IdentityAuthenticationHandler>(IdentityAuthentication.Scheme, null);
        services.AddAuthorization();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup("/identity")
            .WithMetadata(new ModuleEndpointMetadata(typeof(IdentityModule)), new PublicModuleEndpointMetadata());
        group.AddEndpointFilter((context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return next(context);
        });
        group.MapPost("/owner/bootstrap", BootstrapAsync).AllowAnonymous();
        group.MapPost("/login/{provider}", LoginAsync).AllowAnonymous();
        group.MapPost("/link/redeem/{provider}", RedeemAsync).AllowAnonymous();
        group.MapGet("/session", (HttpContext context) => Results.Ok(context.Items[IdentityAuthentication.AuthenticatedIdentity]))
            .RequireAuthorization();
        group.MapPost("/link", async (HttpContext context, IdentityService identities) =>
            Results.Ok(new { code = await identities.CreateLinkCodeAsync(Token(context), TimeSpan.FromMinutes(5)).ConfigureAwait(false) }))
            .RequireAuthorization();
        group.MapPost("/logout", async (HttpContext context, IdentityService identities) =>
        {
            await identities.RevokeSessionAsync(Token(context)).ConfigureAwait(false);
            context.Response.Cookies.Delete(IdentityAuthentication.CookieName, CookieOptions());
            return Results.NoContent();
        }).RequireAuthorization();
        group.MapPost("/grants", async (AutomationGrant request, HttpContext context, IdentityService identities) =>
        {
            try
            {
                var grantId = await identities.GrantAutomationAsync(Token(context), request.WorkspaceId, request.AutomationId,
                    request.Targets, request.Actions).ConfigureAwait(false);
                return Results.Ok(new { grantId });
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException) { return Results.BadRequest(); }
        }).RequireAuthorization();
        group.MapDelete("/grants/{grantId}", async (string grantId, HttpContext context, IdentityService identities) =>
        {
            try { await identities.RevokeAutomationAsync(Token(context), grantId).ConfigureAwait(false); return Results.NoContent(); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization();
    }

    private static async Task<IResult> BootstrapAsync(HttpContext context, IdentityService identities, IConfiguration configuration)
    {
        if (context.RequestServices.GetServices<ModuleEndpointListener>().Any(listener => listener.Port == context.Connection.LocalPort))
        { return Results.NotFound(); }
        var username = configuration["DigitalBrain:Auth:Username"];
        var password = configuration["DigitalBrain:Auth:Password"];
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)
            || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) || header.Length > 1024)
        { return Results.Unauthorized(); }
        byte[] credential;
        try { credential = Convert.FromBase64String(header[6..].Trim()); }
        catch (FormatException) { return Results.Unauthorized(); }
        if (!CryptographicOperations.FixedTimeEquals(credential, Encoding.UTF8.GetBytes($"{username}:{password}")))
        { return Results.Unauthorized(); }
        try
        {
            return Session(context, await identities.BootstrapOwnerAsync(new("owner", username), "owner", SessionLifetime).ConfigureAwait(false));
        }
        catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    }

    private static async Task<IResult> LoginAsync(string provider, ExternalLogin request, HttpContext context, IdentityService identities)
    {
        var verified = await VerifyAsync(provider, request.Credential, context).ConfigureAwait(false);
        if (verified is null) { return Results.Unauthorized(); }
        var session = await identities.SignInAsync(verified, SessionLifetime).ConfigureAwait(false);
        return session is null ? Results.Unauthorized() : Session(context, session);
    }

    private static async Task<IResult> RedeemAsync(string provider, LinkRedemption request, HttpContext context, IdentityService identities)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 512) { return Results.BadRequest(); }
        var verified = await VerifyAsync(provider, request.Credential, context).ConfigureAwait(false);
        if (verified is null) { return Results.Unauthorized(); }
        var session = await identities.RedeemLinkCodeAsync(request.Code, verified, SessionLifetime).ConfigureAwait(false);
        return session is null ? Results.Unauthorized() : Session(context, session);
    }

    private static async ValueTask<VerifiedExternalIdentity?> VerifyAsync(string provider, string credential, HttpContext context)
    {
        if (provider == "owner" || string.IsNullOrWhiteSpace(credential) || credential.Length > 16384) { return null; }
        var verifiers = context.RequestServices.GetServices<IExternalIdentityVerifier>().Where(item => item.Provider == provider).ToArray();
        if (verifiers.Length != 1) { return null; }
        var identity = await verifiers[0].VerifyAsync(credential, context.RequestAborted).ConfigureAwait(false);
        return identity?.Provider == provider ? identity : null;
    }

    private static string Token(HttpContext context) => (string)context.Items[IdentityAuthentication.SessionToken]!;
    private static CookieOptions CookieOptions() => new() { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" };
    private static IResult Session(HttpContext context, IdentitySession session)
    {
        var cookie = CookieOptions();
        cookie.Expires = session.ExpiresAt;
        if (context.Request.Headers["X-DigitalBrain-Session-Transport"] != "bearer")
        {
            context.Response.Cookies.Append(IdentityAuthentication.CookieName, session.Token, cookie);
        }
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(session);
    }

    public sealed record ExternalLogin(string Credential);
    public sealed record LinkRedemption(string Credential, string Code);
    /// <summary>Grants every listed action on every listed target within one workspace and automation.</summary>
    public sealed record AutomationGrant(string WorkspaceId, string AutomationId, string[] Targets, string[] Actions);
}
