using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Comms.Core.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetInvoices;

public sealed class GetInvoicesQueryHandler(
    IStripeGateway stripeGateway,
    ICurrentUserContext currentUserContext,
    IUserSubscriptionRepository userSubscriptionRepository)
    : IRequestHandler<GetInvoicesQuery, Result<InvoicesDTO>>
{
    private const string DefaultInvoiceStatus = "paid";

    public async Task<Result<InvoicesDTO>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();
        var effectiveStatus = string.IsNullOrWhiteSpace(request.Status) ? DefaultInvoiceStatus : request.Status;

        var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken)
            ?? user.UserSubscription;
        if (userSubscription?.StripeCustomerId is null)
        {
            return Result.Success(new InvoicesDTO
            {
                Limit = request.Limit,
                StartingAfter = request.StartingAfter,
                Status = effectiveStatus
            });
        }

        string? startingAfterId = null;
        if (!string.IsNullOrWhiteSpace(request.StartingAfter))
        {
            CursorExtensions.TryDecodeCursor(request.StartingAfter, out startingAfterId);
        }

        var invoices = await stripeGateway.GetInvoicesAsync(
            userSubscription.StripeCustomerId,
            request.Limit,
            startingAfterId,
            effectiveStatus,
            cancellationToken);

        invoices.Limit = request.Limit;
        invoices.StartingAfter = request.StartingAfter;
        invoices.Status = effectiveStatus;

        return Result.Success(invoices);
    }
}
