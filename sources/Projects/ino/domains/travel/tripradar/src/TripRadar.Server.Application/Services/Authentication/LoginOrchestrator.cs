using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Services.Authentication;

public sealed class LoginOrchestrator(
    IUnitOfWork unitOfWork,
    IUserLookupService userLookupService,
    IUserAuthenticationValidator authValidator,
    IPasswordVerificationService passwordVerificationService,
    IAuthenticationTokenIssuer tokenIssuer,
    ILogger<LoginOrchestrator> logger)
    : ILoginOrchestrator
{
    public async Task<Result<AuthenticationModel>> LoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken)
    {
        var maskedIdentifier = MaskLoginIdentifier(usernameOrEmail);
        var userResult = await userLookupService.FindUserAsync(usernameOrEmail, cancellationToken);
        if (userResult.IsFailure)
        {
            logger.LogWarning("Failed login attempt. User not found for identifier: {Identifier}", maskedIdentifier);
            await passwordVerificationService.ConsumeDummyCheckAsync(password, cancellationToken);
            return Result.Failure<AuthenticationModel>(Errors.UsernameOrPasswordNotValid);
        }

        var user = userResult.Value!;
        if (user.Profile.IsLockedOut())
        {
            logger.LogWarning("Login attempt for locked account: {Identifier}", maskedIdentifier);
            var unlockTime = user.Profile.LockoutEnd!.Value;
            return Result.Failure<AuthenticationModel>(Errors.AccountLocked with
            {
                Reason = $"Account is locked until {unlockTime:yyyy-MM-dd HH:mm:ss} UTC"
            });
        }

        var validationResult = authValidator.ValidateForLogin(user);
        if (validationResult.IsFailure)
        {
            return Result.Failure<AuthenticationModel>(validationResult.Error);
        }

        var passwordValid = await passwordVerificationService.VerifyAsync(password, user.Profile.Password, cancellationToken);
        if (!passwordValid)
        {
            await using var failedScope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
            var failedCount = user.Profile.IncrementAccessFailedCount();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await failedScope.CommitAsync(cancellationToken);

            logger.LogWarning("Failed login attempt {Count} for user: {Identifier}", failedCount, maskedIdentifier);
            return Result.Failure<AuthenticationModel>(Errors.UsernameOrPasswordNotValid);
        }

        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        if (user.Profile.AccessFailedCount > 0)
        {
            user.Profile.ResetAccessFailedCount();
        }

        return await tokenIssuer.IssueTokensAsync(user, scope);
    }

    private static string MaskLoginIdentifier(string usernameOrEmail)
    {
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
        {
            return "[empty-identifier]";
        }

        return usernameOrEmail.Contains('@')
            ? StringHelper.MaskEmail(usernameOrEmail)
            : "***";
    }
}
