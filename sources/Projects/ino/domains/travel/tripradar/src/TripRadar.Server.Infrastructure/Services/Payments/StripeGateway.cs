using AutoMapper;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Providers.Stripe.Models;

namespace TripRadar.Server.Infrastructure.Services.Payments;

public class StripeGateway(
    IStripeApiProvider stripeApiProvider,
    IMapper mapper,
    ILogger<StripeGateway> logger) : IStripeGateway
{
    public Task<string> CreateCustomerAsync(string email, string name, CancellationToken ct = default) =>
        stripeApiProvider.CreateCustomerAsync(email, name, ct);

    public async Task<SubscriptionCheckoutIntentDto> CreateSubscriptionCheckoutAsync(string customerId, string priceId, string? couponId, Dictionary<string, string>? subscriptionMetadata, CancellationToken ct = default)
    {
        var result = await stripeApiProvider.CreateSubscriptionCheckoutAsync(customerId, priceId, couponId, subscriptionMetadata, ct);
        logger.LogInformation("Stripe subscription checkout created for customer {CustomerId}", customerId);

        return new SubscriptionCheckoutIntentDto
        {
            ClientSecret = result.ClientSecret,
            AmountSubtotal = result.AmountSubtotal,
            AmountDiscount = result.AmountDiscount,
            AmountTotal = result.AmountTotal,
            Currency = result.Currency
        };
    }

    public Task<(string Status, string? PriceId, DateTime? CurrentPeriodEnd)> GetSubscriptionDetailsAsync(string subscriptionId, CancellationToken ct = default) =>
        stripeApiProvider.GetSubscriptionDetailsAsync(subscriptionId, ct);

    public Task UpdateSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken ct = default) =>
        stripeApiProvider.UpdateSubscriptionPriceAsync(subscriptionId, priceId, ct);

    public Task<string> CreateSetupIntentAsync(string customerId, CancellationToken ct = default) =>
        stripeApiProvider.CreateSetupIntentAsync(customerId, ct);

    public async Task<StripeSubscriptionInfo?> GetSubscriptionByCustomerAsync(string customerId, CancellationToken ct = default)
    {
        var subscription = await stripeApiProvider.GetSubscriptionByCustomerAsync(customerId, ct);
        return subscription is null ? null : mapper.Map<StripeSubscriptionInfo>(subscription);
    }

    public async Task<StripeSubscriptionInfo?> GetSubscriptionByIdAsync(string subscriptionId, CancellationToken ct = default)
    {
        var subscription = await stripeApiProvider.GetSubscriptionByIdAsync(subscriptionId, ct);
        return subscription is null ? null : mapper.Map<StripeSubscriptionInfo>(subscription);
    }

    public async Task<List<StripePaymentMethodInfo>> GetPaymentMethodsAsync(string customerId, CancellationToken ct = default)
    {
        var paymentMethods = await stripeApiProvider.GetPaymentMethodsAsync(customerId, ct);
        return mapper.Map<List<StripePaymentMethodInfo>>(paymentMethods.PaymentMethods);
    }

    public async Task DetachPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default) =>
        await stripeApiProvider.DetachPaymentMethodAsync(customerId, paymentMethodId, ct);

    public async Task SetDefaultPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default) =>
        await stripeApiProvider.SetDefaultPaymentMethodAsync(customerId, paymentMethodId, ct);

    public async Task<bool> HasUnpaidInvoicesAsync(string customerId, CancellationToken ct = default)
    {
        var invoices = await stripeApiProvider.GetInvoicesAsync(customerId, 1, status: "open", cancellationToken: ct);
        return MapInvoiceCount(invoices) > 0;
    }

    public async Task<StripeSubscriptionInfo> ToggleSubscriptionAsync(string subscriptionId, bool activate, CancellationToken ct = default)
    {
        var subscription = await stripeApiProvider.ToggleSubscriptionAsync(subscriptionId, activate, ct);
        return mapper.Map<StripeSubscriptionInfo>(subscription);
    }

    public async Task<StripeUsageSummaryInfo> GetUsageSummaryAsync(string customerId, CancellationToken ct = default)
    {
        var usageSummary = await stripeApiProvider.GetUsageSummaryAsync(customerId, ct);
        return mapper.Map<StripeUsageSummaryInfo>(usageSummary);
    }

    public async Task<InvoicesDTO> GetInvoicesAsync(string customerId, int limit = 20, string? startingAfter = null, string? status = null, CancellationToken ct = default)
    {
        var invoices = await stripeApiProvider.GetInvoicesAsync(customerId, limit, startingAfter, status, ct);
        return new InvoicesDTO
        {
            Invoices = mapper.Map<List<StripeInvoiceInfo>>(invoices.Invoices),
            HasMore = invoices.HasMore,
            NextCursor = invoices.LastInvoiceId
        };
    }

    private static int MapInvoiceCount(InvoicesListResponse invoices) => invoices.Invoices?.Count ?? 0;
}