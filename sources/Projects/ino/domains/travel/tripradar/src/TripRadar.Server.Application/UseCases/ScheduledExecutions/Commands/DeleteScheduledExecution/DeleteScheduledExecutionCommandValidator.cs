using FluentValidation;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Commands.DeleteScheduledExecution;

public class DeleteScheduledExecutionCommandValidator : AbstractValidator<DeleteScheduledExecutionCommand>
{
    public DeleteScheduledExecutionCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters.");

        RuleFor(x => x.ScheduledExecutionUniqueId)
            .NotEmpty().WithMessage("Scheduled execution unique ID is required.");
    }
}
