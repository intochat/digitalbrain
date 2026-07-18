using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Queries.GetPromoCodeByCode;

public class GetPromoCodeByCodeQueryValidator : AbstractValidator<GetPromoCodeByCodeQuery>
{
    public GetPromoCodeByCodeQueryValidator()
    {
        RuleFor(query => query.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters");
    }
}