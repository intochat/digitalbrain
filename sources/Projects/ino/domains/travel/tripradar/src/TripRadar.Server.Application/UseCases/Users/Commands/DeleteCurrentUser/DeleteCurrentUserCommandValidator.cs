using FluentValidation;

namespace TripRadar.Server.Application.UseCases.Users.Commands.DeleteCurrentUser;

public class DeleteCurrentUserCommandValidator : AbstractValidator<DeleteCurrentUserCommand>
{
    public DeleteCurrentUserCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username cannot be empty");
    }
}
