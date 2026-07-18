using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpPlaceFullMenu.Queries.GetYelpPlaceFullMenu;

public class GetYelpPlaceFullMenuQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetYelpPlaceFullMenuQuery>
{
    public Task<Error?> ValidateAsync(GetYelpPlaceFullMenuQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateYelpDomainAsync(request.Request.YelpDomain, cancellationToken);
    }
}
