using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetUserPreferences;

public sealed class GetUserPreferencesQueryValidator : AbstractValidator<GetUserPreferencesQuery>
{
    public GetUserPreferencesQueryValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
    }
}


