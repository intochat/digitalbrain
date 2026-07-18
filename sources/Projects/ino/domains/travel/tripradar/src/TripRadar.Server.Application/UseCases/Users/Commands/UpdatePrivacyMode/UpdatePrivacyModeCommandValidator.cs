using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UpdatePrivacyMode;

public sealed class UpdatePrivacyModeCommandValidator : AbstractValidator<UpdatePrivacyModeCommand>
{
    public UpdatePrivacyModeCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
    }
}
