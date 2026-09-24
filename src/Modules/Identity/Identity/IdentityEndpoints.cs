using System.Security.Claims;
using DigitalBrain.Contracts;
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
    internal const string DefaultWorkspace = "default";
    private const string PrincipalClaim = ClaimTypes.NameIdentifier;
    private const string AccountClaim = "intochat.account";
    private const string WorkspaceClaim = "intochat.workspace";
    private const string RoleClaim = ClaimTypes.Role;

    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/identity/register", RegisterAsync);
        routes.MapPost("/identity/login", LoginAsync);
        routes.MapGet("/identity/session", Session);
        routes.MapPost("/identity/logout", (Delegate)LogoutAsync);
        routes.MapPost("/identity/workspaces", CreateWorkspaceAsync);
        routes.MapGet("/identity/accounts/{accountId}/members", async (string accountId, HttpContext http, IDigitalBrain brain, CancellationToken ct) =>
            http.User.FindFirstValue(AccountClaim) != accountId ? Results.Forbid() : Results.Ok(await Directory(brain).ListMembersAsync(accountId, ct)));
        routes.MapPost("/identity/workspaces/{workspaceId}/invitations", async (string workspaceId, InviteRequest input, HttpContext http, IDigitalBrain brain, CancellationToken ct) =>
        {
            var principal = http.User.FindFirstValue(PrincipalClaim);
            var member = principal is null ? null : await Directory(brain).FindMemberAsync(principal, ct);
            return member?.WorkspaceId != workspaceId || member.Role != MemberRole.Owner
                ? Results.Forbid() : Results.Ok(await Directory(brain).InviteAsync(workspaceId, input.Email, input.Role, ct));
        });
        routes.MapPost("/identity/invitations/{code}/accept", async (string code, AcceptRequest input, HttpContext http, IDigitalBrain brain, CancellationToken ct) =>
            http.User.FindFirstValue(PrincipalClaim) != input.PrincipalId ? Results.Forbid() : Results.Ok(await Directory(brain).AcceptInvitationAsync(code, input.PrincipalId, input.DisplayName, ct)));
        var grants = routes.MapGroup("/workspaces/{workspaceId}/grants")
            .AddEndpointFilter(DigitalBrain.Core.Enforcement.WorkspaceAccessFilter.EnforceAsync);
        grants.MapGet("", async (string workspaceId, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Grants(brain, workspaceId).ListAsync(ct)));
        grants.MapPost("", async (string workspaceId, Grant input, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Grants(brain, workspaceId).GrantAsync(input, ct)));
        grants.MapPost("/revoke", async (string workspaceId, RevokeGrant input, IDigitalBrain brain, CancellationToken ct) =>
        {
            await Grants(brain, workspaceId).RevokeAsync(input.AppId, input.SemanticTypeId, input.Mode, ct);
            return Results.NoContent();
        });
        grants.MapDelete("", async (string workspaceId, string appId, string semanticTypeId, GrantMode mode, IDigitalBrain brain, CancellationToken ct) =>
        {
            await Grants(brain, workspaceId).RevokeAsync(appId, semanticTypeId, mode, ct);
            return Results.NoContent();
        });
    }

    internal static async Task<IResult> CreateWorkspaceAsync(HttpContext http, IDigitalBrain brain, CancellationToken ct)
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
        var workspaceId = "workspace-" + Guid.NewGuid().ToString("N");
        return Results.Ok(await directory.ShareWorkspaceAsync(account, workspaceId, principal, owner.DisplayName, MemberRole.Owner, ct));
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
            new Claim(WorkspaceClaim, member.WorkspaceId),
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
                http.User.FindFirstValue(WorkspaceClaim) ?? "",
                http.User.FindFirstValue(RoleClaim) ?? ""))
            : Results.NoContent();

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static IIdentityDirectory Directory(IDigitalBrain brain) => brain.Get<IIdentityDirectory>(IdentityGrains.Directory);

    private static IGrantStore Grants(IDigitalBrain brain, string workspaceId) => brain.Get<IGrantStore>(IdentityGrains.Grants(workspaceId));

    internal sealed record LoginRequest(string PrincipalId, string? Password = null);
    internal sealed record RegisterRequest(string PrincipalId, string? Password = null, string? DisplayName = null);
    internal sealed record InviteRequest(string? Email = null, MemberRole Role = MemberRole.Member);
    internal sealed record AcceptRequest(string PrincipalId, string DisplayName);
    internal sealed record RevokeGrant(string AppId, string SemanticTypeId, GrantMode Mode);
    internal sealed record SessionView(string PrincipalId, string AccountId, string WorkspaceId, string Role);
}
