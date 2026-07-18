using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Comms.Core.Errors;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.Contracts.Services.Providers;

public interface ISerpApiQueryExecutor
{
    Task<Result<TResponse>> ExecuteAsync<TRequest, TResponse>(TRequest request, ServiceType serviceType, Error notFoundError, CancellationToken cancellationToken) where TRequest : class, ISerpApiRequest;
}
