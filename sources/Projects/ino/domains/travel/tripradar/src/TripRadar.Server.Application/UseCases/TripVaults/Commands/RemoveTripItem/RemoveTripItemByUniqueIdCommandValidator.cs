using FluentValidation;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.RemoveTripItem;

public class RemoveTripItemByUniqueIdCommandValidator : AbstractValidator<RemoveTripItemByUniqueIdCommand>
{
    public RemoveTripItemByUniqueIdCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username is required");

        RuleFor(x => x.TripVaultUniqueId)
            .NotEmpty()
            .WithMessage("Trip vault unique ID is required");

        RuleFor(x => x.ItemUniqueId)
            .NotEmpty()
            .WithMessage("Trip item unique ID is required");
    }
}

