using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services.Authentication;

public interface ILoginOrchestrator
{
    Task<Result<AuthenticationModel>> LoginAsync(string usernameOrEmail, string password, CancellationToken cancellationToken);
}
