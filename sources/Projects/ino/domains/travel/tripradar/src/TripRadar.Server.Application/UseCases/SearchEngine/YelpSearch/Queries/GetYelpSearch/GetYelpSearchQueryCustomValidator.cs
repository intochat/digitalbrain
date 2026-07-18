using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpSearch.Queries.GetYelpSearch;

public class GetYelpSearchQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetYelpSearchQuery>
{
    public Task<Error?> ValidateAsync(GetYelpSearchQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateYelpDomainAsync(request.Request.YelpDomain, cancellationToken);
    }
}
