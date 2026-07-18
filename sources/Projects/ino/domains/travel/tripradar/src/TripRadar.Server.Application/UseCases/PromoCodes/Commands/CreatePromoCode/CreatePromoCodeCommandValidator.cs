using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.CreatePromoCode;

public class CreatePromoCodeCommandValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters")
            .Matches(@"^[A-Z0-9]+$").WithMessage("Promo code can only contain uppercase letters and numbers");

        RuleFor(command => command.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));

        RuleFor(command => command.DiscountType)
            .NotNull().WithMessage("Discount type is required");

        RuleFor(command => command.MaxUsageCount)
            .GreaterThan(0).WithMessage("Max usage count must be greater than 0")
            .When(command => command.MaxUsageCount.HasValue);

        RuleFor(command => command.MaxUsagePerUser)
            .GreaterThan(0).WithMessage("Max usage per user must be greater than 0");

        RuleFor(command => command.StartDate)
            .LessThan(command => command.EndDate).WithMessage("Start date must be before end date");
    }
}
