using System.Security.Claims;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity.Directory;
using DigitalBrain.Identity.Grants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Identity;

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
        routes.MapPost("/identity/logout", (Delegate)LogoutAsync);
        routes.MapPost("/identity/brains", CreateBrainAsync);
        var grants = BrainRoutes.Group(routes, "/grants");
        grants.MapGet("", async (string brainId, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Grants(brain, brainId).ListAsync(ct)));
        grants.MapPost("", async (string brainId, Grant input, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Grants(brain, brainId).GrantAsync(input, ct)));
        grants.MapPost("/revoke", async (string brainId, RevokeGrant input, IDigitalBrain brain, CancellationToken ct) =>
        {
            await Grants(brain, brainId).RevokeAsync(input.AppId, input.SemanticTypeId, input.Mode, ct);
            return Results.NoContent();
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
        var directory = Directory(brain);
        var owner = await directory.FindMemberAsync(principal, ct);
        if (owner is null || owner.AccountId != account || owner.Role != MemberRole.Owner)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var brainId = "workspace-" + Guid.NewGuid().ToString("N");
        var created = await directory.ShareBrainAsync(account, brainId, principal, owner.DisplayName, MemberRole.Owner, ct);
        await brain.Get<IBrain>(BrainScope.Create(account, brainId).Id).Establish(new(brainId, account));
        return Results.Ok(created);
    }

    private static async Task<IResult> LoginAsync(LoginRequest input, HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        var member = await Directory(brain).AuthenticateAsync(input.PrincipalId, input.Password ?? "", ct);
        return member is null ? Results.Unauthorized() : await SignInAsync(member, http);
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest input, HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        try
        {
            var member = await Directory(brain).RegisterAsync(input.PrincipalId, input.Password ?? "", input.DisplayName ?? input.PrincipalId, ct);
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

    private static IIdentityDirectory Directory(IDigitalBrain brain) => brain.Get<IIdentityDirectory>(IdentityGrains.Directory);

    private static IGrantStore Grants(IDigitalBrain brain, string brainId) => brain.Get<IGrantStore>(IdentityGrains.Grants(brainId));

    internal sealed record LoginRequest(string PrincipalId, string? Password = null);
    internal sealed record RegisterRequest(string PrincipalId, string? Password = null, string? DisplayName = null);
    internal sealed record RevokeGrant(string AppId, string SemanticTypeId, GrantMode Mode);
    internal sealed record SessionView(string PrincipalId, string AccountId, string BrainId, string Role);
}
