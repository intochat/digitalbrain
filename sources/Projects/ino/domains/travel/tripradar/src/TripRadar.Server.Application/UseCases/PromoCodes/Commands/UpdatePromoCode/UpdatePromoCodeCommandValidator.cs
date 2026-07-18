using FluentValidation;

namespace TripRadar.Server.Application.UseCases.PromoCodes.Commands.UpdatePromoCode;

public class UpdatePromoCodeCommandValidator : AbstractValidator<UpdatePromoCodeCommand>
{
    public UpdatePromoCodeCommandValidator()
    {
        RuleFor(command => command.Code)
            .NotEmpty().WithMessage("Promo code is required")
            .MaximumLength(50).WithMessage("Promo code must not exceed 50 characters");

        RuleFor(command => command.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters")
            .When(command => !string.IsNullOrWhiteSpace(command.Description));

        RuleFor(command => command.MaxUsageCount)
            .GreaterThan(0).WithMessage("Max usage count must be greater than 0")
            .When(command => command.MaxUsageCount.HasValue);

        RuleFor(command => command.MaxUsagePerUser)
            .GreaterThan(0).WithMessage("Max usage per user must be greater than 0")
            .When(command => command.MaxUsagePerUser.HasValue);

        RuleFor(command => command.StartDate)
            .LessThan(command => command.EndDate).WithMessage("Start date must be before end date")
            .When(command => command.StartDate.HasValue && command.EndDate.HasValue);
    }
}