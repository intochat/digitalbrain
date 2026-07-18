using FluentValidation;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Domain;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.CreateTripVault;

public class CreateTripVaultCommandValidator : AbstractValidator<CreateTripVaultCommand>
{
    public CreateTripVaultCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username is required");

        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Trip name is required")
            .MaximumLength(255)
            .WithMessage("Trip name cannot exceed 255 characters")
            .Must(name => !string.Equals(name.Trim(), TripVaultConstants.DefaultVault, StringComparison.OrdinalIgnoreCase))
            .WithMessage("This trip name is reserved.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .WithMessage("Description cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be greater than or equal to start date")
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue);

        RuleFor(x => x.StartDate)
            .Must(startDate => !startDate.HasValue || startDate.Value.Date >= DateTime.UtcNow.Date)
            .WithMessage("Start date cannot be in the past");

        RuleFor(x => x.EndDate)
            .Must(endDate => !endDate.HasValue || endDate.Value.Date >= DateTime.UtcNow.Date)
            .WithMessage("End date cannot be in the past");
    }
}
