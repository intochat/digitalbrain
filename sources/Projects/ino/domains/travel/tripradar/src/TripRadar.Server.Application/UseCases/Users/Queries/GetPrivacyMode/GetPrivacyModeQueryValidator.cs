using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Queries.GetPrivacyMode;

public sealed class GetPrivacyModeQueryValidator : AbstractValidator<GetPrivacyModeQuery>
{
    public GetPrivacyModeQueryValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
    }
}
