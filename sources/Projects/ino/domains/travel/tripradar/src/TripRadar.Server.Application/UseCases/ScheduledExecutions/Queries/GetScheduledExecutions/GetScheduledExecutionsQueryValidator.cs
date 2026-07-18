using FluentValidation;

namespace TripRadar.Server.Application.UseCases.ScheduledExecutions.Queries.GetScheduledExecutions;

public class GetScheduledExecutionsQueryValidator : AbstractValidator<GetScheduledExecutionsQuery>
{
    public GetScheduledExecutionsQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(50).WithMessage("Username cannot exceed 50 characters.");
    }
}
