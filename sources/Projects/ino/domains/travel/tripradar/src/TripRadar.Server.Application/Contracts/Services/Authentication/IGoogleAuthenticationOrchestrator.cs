using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface IGoogleAuthenticationOrchestrator
{
    Task<Result<AuthenticationModel>> HandleGoogleLoginAsync(
        string email,
        string firstName,
        string lastName,
        string googleId,
        string? profilePictureUrl,
        Func<User, UnitOfWorkTransactionScope, Task<Result<AuthenticationModel>>> issueTokensCallback);
}
