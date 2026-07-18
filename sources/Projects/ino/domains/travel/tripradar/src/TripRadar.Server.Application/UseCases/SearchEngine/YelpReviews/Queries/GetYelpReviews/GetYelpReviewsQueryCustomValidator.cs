using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Application.UseCases.SearchEngine.YelpReviews.Queries.GetYelpReviews;

public class GetYelpReviewsQueryCustomValidator(IReferenceLookupValidator referenceLookupValidator)
    : ICustomRequestValidator<GetYelpReviewsQuery>
{
    public async Task<Error?> ValidateAsync(GetYelpReviewsQuery request, CancellationToken cancellationToken)
    {
        var domainError = await referenceLookupValidator.ValidateYelpDomainAsync(request.Request.YelpDomain, cancellationToken);
        if (domainError is not null)
        {
            return domainError;
        }

        return await referenceLookupValidator.ValidateYelpReviewLanguageAsync(request.Request.Language, cancellationToken);
    }
}
