using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpPlace.Queries.GetYelpPlace;

public class GetYelpPlaceQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetYelpPlaceQuery>
{
    public Task<Error?> ValidateAsync(GetYelpPlaceQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateYelpDomainAsync(request.Request.YelpDomain, cancellationToken);
    }
}
