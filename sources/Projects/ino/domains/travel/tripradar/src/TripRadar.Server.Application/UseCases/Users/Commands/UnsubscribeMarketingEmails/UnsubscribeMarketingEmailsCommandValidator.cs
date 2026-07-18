using FluentValidation;
using System.Net.Mail;

namespace TripRadar.Server.Application.UseCases.Users.Commands.UnsubscribeMarketingEmails;

public sealed class UnsubscribeMarketingEmailsCommandValidator : AbstractValidator<UnsubscribeMarketingEmailsCommand>
{
    public UnsubscribeMarketingEmailsCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Username) || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Username or email is required");

        RuleFor(x => x.Username)
            .Length(1, 255)
            .WithMessage("Username must be between 1 and 255 characters")
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
        
        RuleFor(x => x.Email)
            .Must(BeValidEmail)
            .WithMessage("Email format is invalid")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }

    private static bool BeValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
