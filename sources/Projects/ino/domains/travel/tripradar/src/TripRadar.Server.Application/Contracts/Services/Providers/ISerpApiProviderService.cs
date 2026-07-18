using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.Contracts.Services.Providers;

public interface ISerpApiProviderService
{
    Task<Result<TResponse>> SearchAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : ISerpApiRequest;
}
