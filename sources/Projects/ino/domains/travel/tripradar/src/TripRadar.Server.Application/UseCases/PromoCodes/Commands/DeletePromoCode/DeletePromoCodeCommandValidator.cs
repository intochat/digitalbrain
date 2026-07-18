using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.DeletePromoCode;

public class DeletePromoCodeCommandValidator : AbstractValidator<DeletePromoCodeCommand>
{
    public DeletePromoCodeCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters");
    }
}