using Microsoft.AspNetCore.Http;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Services;

public sealed class AuthenticatedUserResolver(
    IUserRepository userRepository,
    IHttpContextAccessor httpContextAccessor,
    IUserAccessValidator userAccessValidator)
    : IAuthenticatedUserResolver
{
    private const string CurrentUserHttpContextItemKey = "__current_user";

    public async Task<Result<User>> ResolveValidatedUserAsync(string usernameOrEmail, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var user = await ResolveUserFromPrincipalAsync(principal, cancellationToken)
                   ?? await ResolveUserByIdentifierAsync(usernameOrEmail, cancellationToken);

        if (user is null)
        {
            return Result.Failure<User>(Errors.UserNotFound);
        }

        var accessValidationResult = userAccessValidator.Validate(user);
        return accessValidationResult.IsFailure
            ? Result.Failure<User>(accessValidationResult.Error)
            : Result.Success(user);
    }

    public bool IsRequestIdentityMismatch(User user, string requestUsername)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (principal.TryGetUserId(out var userId))
        {
            return user.Id != userId;
        }

        var usernameClaim = principal.GetUsername();
        if (string.IsNullOrWhiteSpace(usernameClaim))
        {
            return false;
        }

        var expectedUsername = string.IsNullOrWhiteSpace(user.Profile.Username)
            ? requestUsername
            : user.Profile.Username;

        if (string.IsNullOrWhiteSpace(expectedUsername))
        {
            return false;
        }

        return !string.Equals(usernameClaim, expectedUsername, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<User?> ResolveUserFromPrincipalAsync(System.Security.Claims.ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue(CurrentUserHttpContextItemKey, out var cachedUserObject) == true &&
            cachedUserObject is User cachedUser)
        {
            return cachedUser;
        }

        if (!principal.TryGetUserId(out var userId))
        {
            return null;
        }

        return await userRepository.GetByIdWithProfileAsync(userId, cancellationToken);
    }

    private async Task<User?> ResolveUserByIdentifierAsync(string usernameOrEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
        {
            return null;
        }

        if (usernameOrEmail.Contains('@'))
        {
            return await userRepository.GetAuthByEmailAsync(usernameOrEmail, cancellationToken);
        }

        var user = await userRepository.GetAuthByUsernameAsync(usernameOrEmail, cancellationToken);
        return user ?? await userRepository.GetAuthByEmailAsync(usernameOrEmail, cancellationToken);
    }
}
