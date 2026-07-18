using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Services.Authentication;

public sealed class RefreshTokenOrchestrator(
    IUnitOfWork unitOfWork,
    IUserAuthenticationValidator authValidator,
    ICredentialValidator credentialValidator,
    IAuthenticationTokenIssuer tokenIssuer)
    : IRefreshTokenOrchestrator
{
    public async Task<Result<AuthenticationModel>> RefreshAsync(long userId, string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || !credentialValidator.IsValidTokenFormat(refreshToken))
        {
            return Result.Failure<AuthenticationModel>(Errors.RefreshTokenInvalidFormat);
        }

        await using var scope = await unitOfWork.StartScopeAsync(cancellationToken: cancellationToken);
        var user = await unitOfWork.UserRepository.GetByIdWithProfileAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AuthenticationModel>(Errors.UserNotFound);
        }

        var validationResult = authValidator.ValidateRefreshToken(user, refreshToken);
        if (validationResult.IsFailure)
        {
            return Result.Failure<AuthenticationModel>(validationResult.Error);
        }

        return await tokenIssuer.IssueTokensAsync(user, scope);
    }
}
