using FluentValidation;
using TripRadar.Server.Comms.Core.Extensions;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetInvoices;

public sealed class GetInvoicesQueryValidator : AbstractValidator<GetInvoicesQuery>
{
    public GetInvoicesQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.StartingAfter)
            .Must(BeValidCursor)
            .When(x => !string.IsNullOrWhiteSpace(x.StartingAfter))
            .WithMessage("Invalid pagination cursor.");
    }

    private static bool BeValidCursor(string? cursor) => string.IsNullOrWhiteSpace(cursor) || CursorExtensions.TryDecodeCursor(cursor, out _);
}

