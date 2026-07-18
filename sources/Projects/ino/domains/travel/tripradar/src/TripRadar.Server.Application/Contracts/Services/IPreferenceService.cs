using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IPreferenceService
{
    Task<Result<TRequest>> AddPreferencesAsync<TRequest>(
        TRequest request,
        long userId,
        ServiceType serviceType,
        CancellationToken cancellationToken = default) where TRequest : class;
}
