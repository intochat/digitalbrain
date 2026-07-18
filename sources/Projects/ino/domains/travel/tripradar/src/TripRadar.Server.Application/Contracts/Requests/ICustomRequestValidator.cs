using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.Contracts.Requests;

public interface ICustomRequestValidator<in TRequest>
{
    Task<Error?> ValidateAsync(TRequest request, CancellationToken cancellationToken);
}
