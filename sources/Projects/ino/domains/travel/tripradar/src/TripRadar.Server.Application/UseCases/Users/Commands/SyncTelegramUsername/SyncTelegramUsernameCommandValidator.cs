using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.SyncTelegramUsername;

public class SyncTelegramUsernameCommandValidator : AbstractValidator<SyncTelegramUsernameCommand>
{
    public SyncTelegramUsernameCommandValidator()
    {
        RuleFor(command => command.TelegramAuth)
            .NotNull().WithMessage("Telegram authentication data is required");

        RuleFor(command => command.TelegramAuth.Id)
            .GreaterThan(0).WithMessage("Telegram user id is required");

        RuleFor(command => command.TelegramAuth.Username)
            .NotEmpty().WithMessage("Telegram username is required")
            .MaximumLength(100).WithMessage("Username must not exceed 100 characters");
    }
}
