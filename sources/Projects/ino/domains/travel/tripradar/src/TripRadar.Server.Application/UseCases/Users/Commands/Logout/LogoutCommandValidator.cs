using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.Logout;

public class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
    }
}
