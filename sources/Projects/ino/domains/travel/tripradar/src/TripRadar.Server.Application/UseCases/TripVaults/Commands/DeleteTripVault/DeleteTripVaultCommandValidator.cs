using FluentValidation;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.DeleteTripVault;

public class DeleteTripVaultCommandValidator : AbstractValidator<DeleteTripVaultCommand>
{
    public DeleteTripVaultCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username is required");

        RuleFor(x => x.TripVaultId)
            .NotEmpty()
            .WithMessage("Trip vault ID is required");
    }
}
