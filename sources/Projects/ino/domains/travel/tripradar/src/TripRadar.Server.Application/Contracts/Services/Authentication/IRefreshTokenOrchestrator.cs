using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface IRefreshTokenOrchestrator
{
    Task<Result<AuthenticationModel>> RefreshAsync(long userId, string refreshToken, CancellationToken cancellationToken);
}
