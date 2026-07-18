using FluentValidation;

namespace TripRadar.Server.Application.UseCases.TripVaults.Commands.RemoveTripItem;

public class RemoveTripItemCommandValidator : AbstractValidator<RemoveTripItemCommand>
{
    public RemoveTripItemCommandValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .WithMessage("Username is required");

        RuleFor(x => x.TripVaultUniqueId)
            .NotEmpty()
            .WithMessage("Trip vault unique ID is required");

        RuleFor(x => x.ItemId)
            .GreaterThan(0)
            .WithMessage("Item ID must be greater than 0");
    }
}
