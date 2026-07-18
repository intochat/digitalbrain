using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeUsageHistory;

public class GetPromoCodeUsageHistoryQueryValidator : AbstractValidator<GetPromoCodeUsageHistoryQuery>
{
    public GetPromoCodeUsageHistoryQueryValidator()
    {
        RuleFor(query => query.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters");
    }
}