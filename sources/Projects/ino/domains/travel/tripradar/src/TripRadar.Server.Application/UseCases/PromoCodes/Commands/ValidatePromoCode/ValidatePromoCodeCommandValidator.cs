using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.ValidatePromoCode;

public class ValidatePromoCodeCommandValidator : AbstractValidator<ValidatePromoCodeCommand>
{
    public ValidatePromoCodeCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters");

        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("Username is required")
            .MaximumLength(100).WithMessage("Username must not exceed 100 characters");
    }
}