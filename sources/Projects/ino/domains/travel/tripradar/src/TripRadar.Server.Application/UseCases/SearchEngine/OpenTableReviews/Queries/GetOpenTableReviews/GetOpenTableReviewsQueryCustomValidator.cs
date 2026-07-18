using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.OpenTableReviews.Queries.GetOpenTableReviews;

public class GetOpenTableReviewsQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetOpenTableReviewsQuery>
{
    public Task<Error?> ValidateAsync(GetOpenTableReviewsQuery request, CancellationToken cancellationToken)
    {
        return referenceLookupValidator.ValidateOpenTableDomainAsync(request.Request.OpenTableDomain, cancellationToken);
    }
}
