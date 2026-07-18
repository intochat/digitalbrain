using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.Contracts.Services.Payments;

public interface IStripeGateway
{
    Task<string> CreateCustomerAsync(string email, string name, CancellationToken ct = default);

    Task<SubscriptionCheckoutIntentDto> CreateSubscriptionCheckoutAsync(string customerId, string priceId, string? couponId, Dictionary<string, string>? subscriptionMetadata, CancellationToken ct = default);

    Task<(string Status, string? PriceId, DateTime? CurrentPeriodEnd)> GetSubscriptionDetailsAsync(string subscriptionId, CancellationToken ct = default);

    Task UpdateSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken ct = default);

    Task<string> CreateSetupIntentAsync(string customerId, CancellationToken ct = default);

    Task<StripeSubscriptionInfo?> GetSubscriptionByCustomerAsync(string customerId, CancellationToken ct = default);

    Task<StripeSubscriptionInfo?> GetSubscriptionByIdAsync(string subscriptionId, CancellationToken ct = default);

    Task<List<StripePaymentMethodInfo>> GetPaymentMethodsAsync(string customerId, CancellationToken ct = default);

    Task DetachPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default);

    Task SetDefaultPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default);

    Task<bool> HasUnpaidInvoicesAsync(string customerId, CancellationToken ct = default);

    Task<StripeSubscriptionInfo> ToggleSubscriptionAsync(string subscriptionId, bool activate, CancellationToken ct = default);

    Task<InvoicesDTO> GetInvoicesAsync(string customerId, int limit = 20, string? startingAfter = null, string? status = null, CancellationToken ct = default);
}