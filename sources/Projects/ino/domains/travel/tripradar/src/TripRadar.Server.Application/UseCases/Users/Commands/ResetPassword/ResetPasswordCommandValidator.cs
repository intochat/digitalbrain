using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.ResetPassword;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage("'Username' is a required field.")
            .MaximumLength(60).WithMessage("'Username' must not exceed 60 characters.")
            .MinimumLength(3).WithMessage("'Username' must be at least 3 characters.")
            .Matches(@"^[a-zA-Z0-9_]+$").WithMessage("'Username' can only contain letters, numbers, and underscores.");

        RuleFor(command => command.Token)
            .NotEmpty().WithMessage("'Token' is a required field.");

        RuleFor(command => command.NewPassword)
            .NotEmpty().WithMessage("'NewPassword' is a required field.")
            .MinimumLength(8).WithMessage("'NewPassword' must be at least 8 characters.")
            .MaximumLength(100).WithMessage("'NewPassword' must not exceed 100 characters.")
            .Matches("[A-Z]").WithMessage("'NewPassword' must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("'NewPassword' must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("'NewPassword' must contain at least one number.")
            .Matches("[^a-zA-Z0-9]").WithMessage("'NewPassword' must contain at least one special character.");
    }
}
