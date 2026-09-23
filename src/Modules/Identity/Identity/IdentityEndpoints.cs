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

// Single-owner login on by default: the login call is itself the account-creation path. Members
// are invited by code and share the owner's workspace; grants are keyed by (app, semantic type,
// mode) in a per-workspace store.
internal static class IdentityEndpoints
{
    internal const string DefaultWorkspace = "default";
    private const string PrincipalClaim = ClaimTypes.NameIdentifier;
    private const string AccountClaim = "intochat.account";
    private const string WorkspaceClaim = "intochat.workspace";
    private const string RoleClaim = ClaimTypes.Role;

    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/identity/login", LoginAsync);
        routes.MapGet("/identity/session", Session);
        routes.MapPost("/identity/logout", (Delegate)LogoutAsync);
        routes.MapGet("/identity/accounts/{accountId}/members", async (string accountId, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Directory(brain).ListMembersAsync(accountId, ct)));
        routes.MapPost("/identity/workspaces/{workspaceId}/invitations", async (string workspaceId, InviteRequest input, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Directory(brain).InviteAsync(workspaceId, input.Email, input.Role, ct)));
        routes.MapPost("/identity/invitations/{code}/accept", async (string code, AcceptRequest input, IDigitalBrain brain, CancellationToken ct) =>
            Results.Ok(await Directory(brain).AcceptInvitationAsync(code, input.PrincipalId, input.DisplayName, ct)));
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

    private static async Task<IResult> LoginAsync(LoginRequest input, HttpContext http, IDigitalBrain brain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.PrincipalId) || input.PrincipalId.Length > 200 || input.PrincipalId.Any(char.IsControl))
        {
            return Results.BadRequest(new { error = "A principal id of 1-200 characters is required." });
        }

        var workspace = string.IsNullOrWhiteSpace(input.WorkspaceId) ? DefaultWorkspace : input.WorkspaceId;
        var display = string.IsNullOrWhiteSpace(input.DisplayName) ? input.PrincipalId : input.DisplayName;
        var member = await Directory(brain).EnsureOwnerAsync(input.PrincipalId, workspace, display, ct);

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

    internal sealed record LoginRequest(string PrincipalId, string? DisplayName = null, string? WorkspaceId = null);
    internal sealed record InviteRequest(string? Email = null, MemberRole Role = MemberRole.Member);
    internal sealed record AcceptRequest(string PrincipalId, string DisplayName);
    internal sealed record RevokeGrant(string AppId, string SemanticTypeId, GrantMode Mode);
    internal sealed record SessionView(string PrincipalId, string AccountId, string WorkspaceId, string Role);
}
