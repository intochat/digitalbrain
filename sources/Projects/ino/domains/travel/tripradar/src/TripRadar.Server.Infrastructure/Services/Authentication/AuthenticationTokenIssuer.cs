using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Authentication;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Services.Authentication;

public class AuthenticationTokenIssuer(
    IUnitOfWork unitOfWork,
    ITokenService tokenService) : IAuthenticationTokenIssuer
{
    public async Task<Result<AuthenticationModel>> IssueTokensAsync(User user, UnitOfWorkTransactionScope scope)
    {
        var (accessToken, refreshToken) = tokenService.RotateRefreshToken(user);
        await unitOfWork.UserRepository.UpdateRefreshTokenAsync(user);
        await scope.CommitAsync();

        return Result.Success(new AuthenticationModel { Token = accessToken, RefreshToken = refreshToken });
    }
}
