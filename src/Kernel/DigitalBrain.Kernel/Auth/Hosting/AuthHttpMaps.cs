using System.Security.Claims;
using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace DigitalBrain.Kernel;

internal static class AuthHttpMaps
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
            HttpSurfacePaths.AuthBootstrapPath,
            static async Task<IResult> (
                HttpContext http,
                AuthCredentialsRequest request,
                UserManager<DigitalBrainUser> users,
                IUserClaimsPrincipalFactory<DigitalBrainUser> principals,
                IAccountDirectory accounts,
                IWorkspaceMembershipGateway workspace,
                TimeProvider time,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(users);
                ArgumentNullException.ThrowIfNull(principals);
                ArgumentNullException.ThrowIfNull(accounts);
                ArgumentNullException.ThrowIfNull(workspace);

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "username and password are required." });
                }

                if (!await accounts.IsEmptyAsync(cancellationToken).ConfigureAwait(false))
                {
                    return Results.Conflict(new { error = "Bootstrap is refused because an Owner already exists." });
                }

                var principalId = PrincipalId.New();
                var user = new DigitalBrainUser
                {
                    UserName = request.Username.Trim(),
                    PrincipalId = principalId.Value,
                    IsBootstrapOwner = true,
                    CreatedAt = time.GetUtcNow(),
                };

                var created = await users.CreateAsync(user, request.Password).ConfigureAwait(false);
                if (!created.Succeeded)
                {
                    return Results.BadRequest(new
                    {
                        error = string.Join("; ", created.Errors.Select(error => error.Description)),
                    });
                }

                user = await users.FindByIdAsync(user.Id).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Bootstrap account vanished after create.");

                var actor = new ActorContext(principalId, user.UserName);
                await workspace.AddMemberAsync(
                    actor,
                    principalId,
                    user.UserName,
                    WorkspaceRole.Owner,
                    cancellationToken).ConfigureAwait(false);

                await SignInAsync(http, user, principals).ConfigureAwait(false);
                return Results.Ok(ToMe(user));
            }).AllowAnonymous();

        endpoints.MapPost(
            HttpSurfacePaths.AuthLoginPath,
            static async Task<IResult> (
                HttpContext http,
                AuthCredentialsRequest request,
                UserManager<DigitalBrainUser> users,
                IUserClaimsPrincipalFactory<DigitalBrainUser> principals,
                IPasswordHasher<DigitalBrainUser> passwords,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(users);
                ArgumentNullException.ThrowIfNull(principals);
                ArgumentNullException.ThrowIfNull(passwords);
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "username and password are required." });
                }

                var user = await users.FindByNameAsync(request.Username.Trim()).ConfigureAwait(false);
                if (user is null || string.IsNullOrEmpty(user.PasswordHash))
                {
                    return Results.Unauthorized();
                }

                var verification = passwords.VerifyHashedPassword(user, user.PasswordHash, request.Password);
                if (verification == PasswordVerificationResult.Failed)
                {
                    return Results.Unauthorized();
                }

                if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                {
                    user.PasswordHash = passwords.HashPassword(user, request.Password);
                    await users.UpdateAsync(user).ConfigureAwait(false);
                }

                await SignInAsync(http, user, principals).ConfigureAwait(false);
                return Results.Ok(ToMe(user));
            }).AllowAnonymous();

        endpoints.MapPost(
            HttpSurfacePaths.AuthLogoutPath,
            static async Task<IResult> (HttpContext http) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                await http.SignOutAsync(AuthHostingExtensions.AuthenticationScheme).ConfigureAwait(false);
                return Results.NoContent();
            }).AllowAnonymous();

        endpoints.MapGet(
            HttpSurfacePaths.AuthMePath,
            static IResult (HttpContext http) =>
            {
                ArgumentNullException.ThrowIfNull(http);

                if (!HttpActor.TryGet(http, out var actor))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new AuthMeResponse(
                    actor.Username,
                    actor.PrincipalId.Value.ToString("N"),
                    http.User.HasClaim(AuthOptions.BootstrapOwnerClaimType, "1")));
            }).AllowAnonymous();

        endpoints.MapPost(
            HttpSurfacePaths.AuthUsersPath,
            static async Task<IResult> (
                HttpContext http,
                AuthCreateUserRequest request,
                UserManager<DigitalBrainUser> users,
                IWorkspaceMembershipGateway workspace,
                TimeProvider time,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(request);
                ArgumentNullException.ThrowIfNull(users);
                ArgumentNullException.ThrowIfNull(workspace);

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.BadRequest(new { error = "username and password are required." });
                }

                if (!Enum.TryParse<WorkspaceRole>(request.Role, ignoreCase: true, out var role)
                    || role is not (WorkspaceRole.Admin or WorkspaceRole.Builder or WorkspaceRole.Viewer or WorkspaceRole.Owner))
                {
                    return Results.BadRequest(new { error = "role must be Owner, Admin, Builder, or Viewer." });
                }

                var actor = HttpActor.Require(http);
                Membership membership;
                try
                {
                    membership = await workspace.ReadMembershipAsync(actor, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return Results.Json(
                        new { error = "Caller is not a workspace member." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var actorMember = membership.Members.FirstOrDefault(member => member.PrincipalId == actor.PrincipalId);
                if (actorMember is null
                    || actorMember.Role is not (WorkspaceRole.Owner or WorkspaceRole.Admin))
                {
                    return Results.Json(
                        new { error = "Only Owner or Admin may create users." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                var principalId = PrincipalId.New();
                var user = new DigitalBrainUser
                {
                    UserName = request.Username.Trim(),
                    PrincipalId = principalId.Value,
                    IsBootstrapOwner = false,
                    CreatedAt = time.GetUtcNow(),
                };

                var created = await users.CreateAsync(user, request.Password).ConfigureAwait(false);
                if (!created.Succeeded)
                {
                    return Results.BadRequest(new
                    {
                        error = string.Join("; ", created.Errors.Select(error => error.Description)),
                    });
                }

                await workspace.AddMemberAsync(
                    actor,
                    principalId,
                    user.UserName,
                    role,
                    cancellationToken).ConfigureAwait(false);

                return Results.Ok(ToMe(user));
            });

        return endpoints;
    }

    private static async Task SignInAsync(
        HttpContext http,
        DigitalBrainUser user,
        IUserClaimsPrincipalFactory<DigitalBrainUser> principals)
    {
        var factoryPrincipal = await principals.CreateAsync(user).ConfigureAwait(false);
        var claims = factoryPrincipal.Claims.ToList();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            AuthHostingExtensions.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));

        await http.SignInAsync(
            AuthHostingExtensions.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
            }).ConfigureAwait(false);
    }

    private static AuthMeResponse ToMe(DigitalBrainUser user)
        => new(user.UserName, user.PrincipalId.ToString("N"), user.IsBootstrapOwner);
}

