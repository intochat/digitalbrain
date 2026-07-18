using AutoMapper;
using MediatR;
using TripRadar.Server.Application.Contracts;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.Extensions;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.UseCases.Payments.Queries.GetPaymentMethods;

public class GetPaymentMethodsQueryHandler(
    IStripeGateway stripeGateway,
    IMapper mapper,
    ICurrentUserContext currentUserContext,
    IUserSubscriptionRepository userSubscriptionRepository)
    : IRequestHandler<GetPaymentMethodsQuery, Result<PaymentMethodsDTO>>
{
    public async Task<Result<PaymentMethodsDTO>> Handle(GetPaymentMethodsQuery request, CancellationToken cancellationToken)
    {
        var user = currentUserContext.GetRequiredUser();

        var userSubscription = await userSubscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken)
            ?? user.UserSubscription;
        if (userSubscription?.StripeCustomerId is null)
        {
            return Result.Success(new PaymentMethodsDTO
            {
                PaymentMethods = [],
                HasActiveSubscription = false
            });
        }

        var paymentMethods = await stripeGateway.GetPaymentMethodsAsync(userSubscription.StripeCustomerId, cancellationToken);

        var stripeSubscription = await stripeGateway.GetSubscriptionByCustomerAsync(userSubscription.StripeCustomerId, cancellationToken);
        var defaultPaymentMethodId = stripeSubscription?.DefaultPaymentMethodId;

        var hasActiveSubscription = Equals(stripeSubscription.GetStatusEnum(), SubscriptionStatusType.Active) || Equals(stripeSubscription.GetStatusEnum(), SubscriptionStatusType.Trialing);

        var uniquePaymentMethods = paymentMethods
            .GroupBy(PaymentMethodIdentity.From)
            .Select(group => group
                .OrderByDescending(pm => pm.Id == defaultPaymentMethodId)
                .ThenByDescending(pm => pm.CreatedAt)
                .First())
            .OrderByDescending(pm => pm.CreatedAt)
            .ToList();

        var paymentMethodItems = uniquePaymentMethods
            .Select(pm =>
            {
                var item = mapper.Map<PaymentMethodItemDTO>(pm);
                item.IsDefault = pm.Id == defaultPaymentMethodId;
                return item;
            })
            .ToList();

        return Result.Success(new PaymentMethodsDTO
        {
            PaymentMethods = paymentMethodItems,
            HasActiveSubscription = hasActiveSubscription
        });
    }

    private readonly record struct PaymentMethodIdentity(string Type, string Brand, string Last4, int ExpMonth, int ExpYear)
    {
        public static PaymentMethodIdentity From(StripePaymentMethodInfo paymentMethod) =>
            new(Normalize(paymentMethod.Type),
                Normalize(paymentMethod.Brand),
                Normalize(paymentMethod.Last4),
                paymentMethod.ExpMonth,
                paymentMethod.ExpYear);

        private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    }
}
