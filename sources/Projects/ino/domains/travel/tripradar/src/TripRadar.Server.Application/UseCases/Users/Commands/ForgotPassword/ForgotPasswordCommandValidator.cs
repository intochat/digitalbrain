using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ForgotPassword;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("'Email' is a required field.")
            .EmailAddress().WithMessage("'Email' must be a valid email address.");
    }
}
