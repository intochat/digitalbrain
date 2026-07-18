using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Aggregates;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface IAuthenticationTokenIssuer
{
    Task<Result<AuthenticationModel>> IssueTokensAsync(User user, UnitOfWorkTransactionScope scope);
}
