using System.Security.Claims;
using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace DigitalBrain.Kernel;

// Single-owner dev auth. The Flutter shell's default credentials (host_environment.dart:
// username "owner" / password "ownerowner") sign in a fixed principal via a cookie. Replaces
// the ASP.NET Identity + Azure Table account store + Workspace membership machinery that used
// to mint and persist per-installation accounts: this kernel only ever serves one owner, so its
// PrincipalId is a stable constant instead of something a table has to remember across restarts.
internal static class DevCookieAuth
{
    public const string AuthenticationScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string PrincipalIdClaimType = "db.principal-id";

    private const string CookieName = "DigitalBrain.Auth";
    private const string DefaultUsername = "owner";
    private const string DefaultPassword = "ownerowner";
    private const string UsernameConfigKey = "DigitalBrain:Auth:DevBootstrapUsername";
    private const string PasswordConfigKey = "DigitalBrain:Auth:DevBootstrapPassword";

    // Fixed, not random: with a single config-defined owner there is nothing to persist, so the
    // constant itself is what survives kernel restarts and keeps the owner's per-principal chat/
    // graph/surface state (PrincipalPartition.InstanceName) stable across them.
    private static readonly PrincipalId OwnerPrincipalId = new(new Guid("0000dead-0000-0000-0000-000000000001"));

    public static IHostApplicationBuilder AddDigitalBrainAuth(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddAuthentication(AuthenticationScheme)
            .AddCookie(AuthenticationScheme, static options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(14);
                options.Events.OnRedirectToLogin = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = static context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        // Everything except /auth/* (AllowAnonymous below) and the health checks (self-declared
        // AllowAnonymous in ServiceDefaultsExtensions) requires the cookie.
        builder.Services.AddAuthorization(static options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return builder;
    }

    public static WebApplication UseDigitalBrainAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.AuthBootstrapPath,
            static async Task<IResult> (HttpContext http, AuthCredentialsRequest request, IConfiguration configuration) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "username and password are required." });
                }

                if (!MatchesOwner(request, configuration))
                {
                    // No account store to conflict with -- the client's own fallback (bootstrap
                    // then login) is what the "already exists" case used to reach anyway.
                    return Results.BadRequest(new { error = "Bootstrap credentials do not match the configured owner." });
                }

                var username = request.Username.Trim();
                await SignInAsync(http, username).ConfigureAwait(false);
                return Results.Ok(ToMe(username));
            }).AllowAnonymous();

        endpoints.MapPost(
            HttpSurfacePaths.AuthLoginPath,
            static async Task<IResult> (HttpContext http, AuthCredentialsRequest request, IConfiguration configuration) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "username and password are required." });
                }

                if (!MatchesOwner(request, configuration))
                {
                    return Results.Unauthorized();
                }

                var username = request.Username.Trim();
                await SignInAsync(http, username).ConfigureAwait(false);
                return Results.Ok(ToMe(username));
            }).AllowAnonymous();

        endpoints.MapGet(
            HttpSurfacePaths.AuthMePath,
            static IResult (HttpContext http) =>
            {
                ArgumentNullException.ThrowIfNull(http);

                return HttpActor.TryGet(http, out var actor)
                    ? Results.Ok(ToMe(actor.Username))
                    : Results.Unauthorized();
            }).AllowAnonymous();

        return endpoints;
    }

    private static bool MatchesOwner(AuthCredentialsRequest request, IConfiguration configuration)
    {
        var username = configuration[UsernameConfigKey];
        if (string.IsNullOrWhiteSpace(username))
        {
            username = DefaultUsername;
        }

        var password = configuration[PasswordConfigKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            password = DefaultPassword;
        }

        return string.Equals(request.Username.Trim(), username, StringComparison.Ordinal)
            && string.Equals(request.Password, password, StringComparison.Ordinal);
    }

    private static async Task SignInAsync(HttpContext http, string username)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, username),
                new Claim(PrincipalIdClaimType, OwnerPrincipalId.Value.ToString("N")),
            ],
            AuthenticationScheme);

        await http.SignInAsync(
            AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true }).ConfigureAwait(false);
    }

    private static AuthMeResponse ToMe(string username)
        // The single owner this kernel serves is definitionally the bootstrap owner.
        => new(username, OwnerPrincipalId.Value.ToString("N"), IsBootstrapOwner: true);
}

internal sealed record AuthCredentialsRequest(string Username, string Password);

internal sealed record AuthMeResponse(string Username, string PrincipalId, bool IsBootstrapOwner);
