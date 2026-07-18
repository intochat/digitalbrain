using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserProfileByUsername;

public sealed class GetUserProfileByUsernameQueryValidator : AbstractValidator<GetUserProfileByUsernameQuery>
{
    public GetUserProfileByUsernameQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username is required")
            .Length(1, 255)
            .WithMessage("Username must be between 1 and 255 characters");
    }
}
