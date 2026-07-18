using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.ApplyPromoCode;

public class ApplyPromoCodeCommandValidator : AbstractValidator<ApplyPromoCodeCommand>
{
    public ApplyPromoCodeCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters");

        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("Username is required")
            .MaximumLength(100).WithMessage("Username must not exceed 100 characters");

        RuleFor(command => command.OrderAmount)
            .GreaterThan(0).WithMessage("Order amount must be greater than 0");
    }
}