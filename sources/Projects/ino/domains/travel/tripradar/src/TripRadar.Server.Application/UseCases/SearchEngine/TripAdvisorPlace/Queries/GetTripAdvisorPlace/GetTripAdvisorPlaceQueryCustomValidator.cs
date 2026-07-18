using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorPlace.Queries.GetTripAdvisorPlace;

public class GetTripAdvisorPlaceQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetTripAdvisorPlaceQuery>
{
    public Task<Error?> ValidateAsync(GetTripAdvisorPlaceQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateTripAdvisorDomainAsync(request.Request.TripadvisorDomain, cancellationToken);
    }
}
