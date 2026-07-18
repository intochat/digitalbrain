using FluentValidation;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.UpdateScheduledExecutionConfiguration;

public class UpdateScheduledExecutionConfigurationCommandValidator : AbstractValidator<UpdateScheduledExecutionConfigurationCommand>
{
    public UpdateScheduledExecutionConfigurationCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters.");

        RuleFor(x => x.ScheduledExecutionUniqueId)
            .NotEmpty().WithMessage("Scheduled execution unique ID is required.");

        RuleFor(x => x.Schedule)
            .Must(schedule => string.IsNullOrWhiteSpace(schedule) || schedule.Trim().Length >= 5)
            .WithMessage("Schedule must be at least 5 characters.")
            .Must(schedule => string.IsNullOrWhiteSpace(schedule) || schedule.Trim().Length <= 100)
            .WithMessage("Schedule cannot exceed 100 characters.");

        RuleFor(x => x.NextExecutionTime)
            .Must(nextExecutionTime => !nextExecutionTime.HasValue || nextExecutionTime.Value > DateTime.UtcNow)
            .WithMessage("Next execution time must be in the future.");
    }
}
