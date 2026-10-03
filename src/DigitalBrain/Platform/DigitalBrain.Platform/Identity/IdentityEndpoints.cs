using DigitalBrain.Platform.Identity.Authority;
using DigitalBrain.Kernel.AspNetCore;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using DigitalBrain.Platform.Identity.Configuration;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Directory;
using DigitalBrain.Platform.Identity.Grants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Platform.Identity;

// Password-authenticated accounts own independent default workspaces. Invitation membership
// and grants remain scoped to the owning account and workspace.
internal static class IdentityEndpoints
{
    private const string PrincipalClaim = ClaimTypes.NameIdentifier;
    private const string AccountClaim = "intochat.account";
    private const string BrainClaim = "intochat.brain";
    private const string RoleClaim = ClaimTypes.Role;

    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet(AccountSession.CheckPath, static () => Results.NoContent());
        routes.MapPost("/identity/register", RegisterAsync);
        routes.MapPost("/identity/login", LoginAsync);
        routes.MapGet("/identity/session", Session);
        routes.MapPost("/identity/session/brain", SelectBrainAsync);
        routes.MapPost("/identity/logout", (Delegate)LogoutAsync);
        routes.MapPost("/identity/brains", CreateBrainAsync);
        var grants = BrainRoutes.Group(routes, "/grants");
        grants.MapGet("", async (string brainId, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Grants(brain, brainId).ListGrants().WaitAsync(ct)));
        grants.MapPost("", async (string brainId, Grant input, IDigitalBrain brain, CancellationToken ct) =>
        {
            try { return Results.Ok(await Grants(brain, brainId).GrantAsOwner(CallerContextStamper.Require() with { BrainId = brainId }, input).WaitAsync(ct)); }
            catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        });
        grants.MapPost("/revoke", async (string brainId, RevokeGrant input, IDigitalBrain brain, CancellationToken ct) =>
        {
            try
            {
                await Grants(brain, brainId).RevokeAsOwner(CallerContextStamper.Require() with { BrainId = brainId }, input.AppId, input.SemanticTypeId, input.Mode).WaitAsync(ct);
                return Results.NoContent();
            }
            catch (UnauthorizedAccessException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        });
    }

    internal static async Task<IResult> CreateBrainAsync(HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        var principal = http.User.FindFirstValue(PrincipalClaim);
        var account = http.User.FindFirstValue(AccountClaim);
        if (http.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(principal) || string.IsNullOrEmpty(account))
        {
            return Results.Unauthorized();
        }
        var owner = await brain.Get<IAccount>(account).Read().WaitAsync(ct);
        if (owner?.OwnerPrincipalId != principal) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
        var operationId = http.Request.Headers["Idempotency-Key"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        if (operationId.Length > 200) { return Results.BadRequest(new { error = "Idempotency-Key is too long." }); }
        var created = await brain.Get<IAccount>(account).CreateBrain(principal, operationId, owner.Name).WaitAsync(ct);
        return Results.Ok(created);
    }

    private static async Task<IResult> SelectBrainAsync(BrainSelection input, HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        var principal = http.User.FindFirstValue(PrincipalClaim);
        if (http.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(principal))
        { return Results.Unauthorized(); }
        if (string.IsNullOrWhiteSpace(input.AccountId) || !BrainScope.IsValidId(input.BrainId))
        { return Results.BadRequest(); }
        var member = await brain.Get<IBrainAuthority>(BrainScope.Create(input.AccountId, input.BrainId).Id)
            .Membership(principal).WaitAsync(ct);
        return member is null ? Results.StatusCode(StatusCodes.Status403Forbidden) : await SignInAsync(member, http);
    }

    private static async Task<IResult> LoginAsync(LoginRequest input, HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.PrincipalId)) { return Results.Unauthorized(); }
        var member = await brain.Get<IPrincipal>(input.PrincipalId).Authenticate(input.Password ?? "").WaitAsync(ct);
        return member is null ? Results.Unauthorized() : await SignInAsync(member, http);
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest input, HttpContext http, IDigitalBrain brain,
        Microsoft.Extensions.Options.IOptions<Configuration.AuthOptions> auth, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.PrincipalId)) { return Results.BadRequest(new { error = "Username is required." }); }
        if (!string.IsNullOrEmpty(auth.Value.Username) && string.Equals(input.PrincipalId, auth.Value.Username, StringComparison.OrdinalIgnoreCase))
        { return Results.Conflict(new { error = "This username is reserved." }); }
        try
        {
            var member = await brain.Get<IPrincipal>(input.PrincipalId).Register(input.Password ?? "", input.DisplayName ?? input.PrincipalId).WaitAsync(ct);
            return await SignInAsync(member, http);
        }
        catch (ArgumentException error) { return Results.BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
    }

    private static async Task<IResult> SignInAsync(Member member, HttpContext http)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(PrincipalClaim, member.PrincipalId),
            new Claim(AccountClaim, member.AccountId),
            new Claim(BrainClaim, member.BrainId),
            new Claim(RoleClaim, member.Role.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Ok(member);
    }

    private static IResult Session(HttpContext http)
        => http.User.Identity?.IsAuthenticated == true
            ? Results.Ok(new SessionView(
                http.User.FindFirstValue(PrincipalClaim) ?? "",
                http.User.FindFirstValue(AccountClaim) ?? "",
                http.User.FindFirstValue(BrainClaim) ?? "",
                http.User.FindFirstValue(RoleClaim) ?? ""))
            : Results.NoContent();

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static IBrainAuthority Grants(IDigitalBrain brain, string brainId)
        => brain.Get<IBrainAuthority>(BrainScope.Create(CallerContextStamper.Require().AccountId, brainId).Id);

    internal sealed record LoginRequest(string PrincipalId, string? Password = null);
    internal sealed record BrainSelection(string AccountId, string BrainId);
    internal sealed record RegisterRequest(string PrincipalId, string? Password = null, string? DisplayName = null);
    internal sealed record RevokeGrant(string AppId, string SemanticTypeId, GrantMode Mode);
    internal sealed record SessionView(string PrincipalId, string AccountId, string BrainId, string Role);
}
