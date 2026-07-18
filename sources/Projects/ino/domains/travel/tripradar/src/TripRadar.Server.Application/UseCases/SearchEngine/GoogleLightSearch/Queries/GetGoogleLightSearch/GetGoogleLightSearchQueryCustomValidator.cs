using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.GoogleLightSearch.Queries.GetGoogleLightSearch;

public class GetGoogleLightSearchQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetGoogleLightSearchQuery>
{
    public Task<Error?> ValidateAsync(GetGoogleLightSearchQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateGoogleLrAsync(request.Request.Lr, cancellationToken);
    }
}
