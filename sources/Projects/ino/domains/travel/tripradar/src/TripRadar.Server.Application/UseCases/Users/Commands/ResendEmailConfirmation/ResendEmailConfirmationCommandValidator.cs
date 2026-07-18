using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ResendEmailConfirmation;

public class ResendEmailConfirmationCommandValidator : AbstractValidator<ResendEmailConfirmationCommand>
{
    public ResendEmailConfirmationCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("'Email' is a required field.")
            .EmailAddress().WithMessage("'Email' must be a valid email address.");
    }
}
