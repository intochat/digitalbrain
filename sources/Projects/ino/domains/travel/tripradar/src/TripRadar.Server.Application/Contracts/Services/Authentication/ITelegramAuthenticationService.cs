using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface ITelegramAuthenticationService
{
    Task<Result<User>> UpsertUserAsync(TelegramAuthDataDTO authData, CancellationToken ct = default);
}
