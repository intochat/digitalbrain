using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.TripAdvisorSearch.Queries.GetTripAdvisorSearch;

public class GetTripAdvisorSearchQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetTripAdvisorSearchQuery>
{
    public Task<Error?> ValidateAsync(GetTripAdvisorSearchQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateTripAdvisorDomainAsync(request.Request.TripadvisorDomain, cancellationToken);
    }
}
