using FluentValidation;
using TripRadar.Server.Application.Constants;

namespace TripRadar.Server.Application.UseCases.TripVaults.Queries.GetTripQueryHistory;

public sealed class GetTripQueryHistoryQueryValidator : AbstractValidator<GetTripQueryHistoryQuery>
{
    public GetTripQueryHistoryQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(ValidationConstants.MinLimit, ValidationConstants.MaxLimit);
    }
}
