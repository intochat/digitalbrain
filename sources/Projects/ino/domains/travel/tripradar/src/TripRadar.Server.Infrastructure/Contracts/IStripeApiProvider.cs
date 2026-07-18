using TripRadar.Server.Infrastructure.Providers.Stripe.Models;

namespace TripRadar.Server.Infrastructure.Contracts;

public interface IStripeApiProvider
{
    Task<StripeSubscriptionCheckoutResult> CreateSubscriptionCheckoutAsync(string customerId, string priceId, string? couponId, Dictionary<string, string>? subscriptionMetadata, CancellationToken cancellationToken = default);

    Task<string> CreateCustomerAsync(string email, string? name = null, CancellationToken cancellationToken = default);

    Task<(string Status, string? CurrentPriceId, DateTime? CurrentPeriodEnd)> GetSubscriptionDetailsAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task UpdateSubscriptionPriceAsync(string subscriptionId, string priceId, CancellationToken cancellationToken = default);

    Task<string> CreateSetupIntentAsync(string customerId, CancellationToken cancellationToken = default);

    Task<(string RefundId, string PaymentIntentId, int Amount, string Currency, string Status, string Reason, DateTime Created, Dictionary<string, string>? Metadata)> CreateRefundAsync(string paymentIntentId, int? amount = null, string reason = "requested_by_customer", Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default);

    Task<string?> GetLatestPaymentIntentFromSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<string> CreateInvoiceItemAsync(string customerId, int amountCents, string currency, string description, Dictionary<string, string>? metadata = null, string? subscriptionId = null, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    Task<string> CreateAndPayInvoiceAsync(string customerId, Dictionary<string, string>? metadata = null, string? idempotencyKey = null, CancellationToken cancellationToken = default);

    Task<SubscriptionResponse?> GetSubscriptionByCustomerAsync(string customerId, CancellationToken cancellationToken = default);

    Task<SubscriptionResponse?> GetSubscriptionByIdAsync(string subscriptionId, CancellationToken cancellationToken = default);

    Task<SubscriptionResponse> ToggleSubscriptionAsync(string subscriptionId, bool activate, CancellationToken cancellationToken = default);

    Task<PaymentMethodsListResponse> GetPaymentMethodsAsync(string customerId, CancellationToken cancellationToken = default);

    Task<DetachPaymentMethodResponse> DetachPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken cancellationToken = default);

    Task<PaymentMethodResponse> SetDefaultPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken cancellationToken = default);

    Task<InvoicesListResponse> GetInvoicesAsync(string customerId, int limit = 20, string? startingAfter = null, string? status = null, CancellationToken cancellationToken = default);

    Task<UsageSummaryResponse> GetUsageSummaryAsync(string customerId, CancellationToken cancellationToken = default);
}