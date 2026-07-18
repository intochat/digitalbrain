using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserTierUsage;

public class GetUserTierUsageQueryValidator : AbstractValidator<GetUserTierUsageQuery>
{
    public GetUserTierUsageQueryValidator()
    {
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.")
            .MinimumLength(3).WithMessage("'Username' must be at least 3 characters.")
            .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("'Username' can only contain letters, numbers, and underscores.");
    }
}
