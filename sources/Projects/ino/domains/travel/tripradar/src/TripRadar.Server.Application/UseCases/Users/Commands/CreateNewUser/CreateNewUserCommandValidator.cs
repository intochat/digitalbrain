using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.CreateNewUser;

public class CreateNewUserCommandValidator : AbstractValidator<CreateNewUserCommand>
{
    public CreateNewUserCommandValidator()
    {
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("'Password' is a required field.")
            .MinimumLength(8).WithMessage("Password does not meet requirements")
            .MaximumLength(100).WithMessage("Password does not meet requirements")
            .Matches("[A-Z]").WithMessage("Password does not meet requirements")
            .Matches("[a-z]").WithMessage("Password does not meet requirements")
            .Matches("[0-9]").WithMessage("Password does not meet requirements")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password does not meet requirements");

        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email format")
            .Matches(@"^[^@\s]+@[^@\s]+\.[^@\s]+$").WithMessage("Invalid email format")
            .MaximumLength(255).WithMessage("Email must not exceed 255 characters");

        RuleFor(command => command.FirstName)
            .MaximumLength(50).WithMessage("'FirstName' must not exceed 50 characters.")
            .Matches(@"^[\p{L}\s'\-]+$").WithMessage("'FirstName' can only contain letters, spaces, and hyphens.")
            .When(command => !string.IsNullOrWhiteSpace(command.FirstName));

        RuleFor(command => command.LastName)
            .MaximumLength(50).WithMessage("'LastName' must not exceed 50 characters.")
            .Matches(@"^[\p{L}\s'\-]+$").WithMessage("'LastName' can only contain letters, spaces, and hyphens.")
            .When(command => !string.IsNullOrWhiteSpace(command.LastName));

        RuleFor(command => command.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{9,14}$").WithMessage("'PhoneNumber' must be a valid phone number.")
            .When(command => !string.IsNullOrWhiteSpace(command.PhoneNumber));

        RuleFor(command => command.HasDataStorageConsent)
            .Equal(true).WithMessage("HasDataStorageConsent must be true to create an account.");
    }
}
