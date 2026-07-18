using DigitalBrain.Abstractions.Bundles;
using System.Text.Json.Serialization;

namespace DigitalBrain.Abstractions.Distribution;

[GenerateSerializer]
public readonly record struct Money
{
    public const string DefaultCurrency = "USD";

    [JsonConstructor]
    public Money(decimal amount, string currency = DefaultCurrency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Money amount must not be negative.");
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
    }

    [Id(0)]
    public decimal Amount { get; }

    [Id(1)]
    public string Currency { get; }

    public static Money Usd(decimal amount) => new(amount);
}

[GenerateSerializer]
public readonly record struct Bips
{
    public const int Min = 0;
    public const int Max = 10000;

    [JsonConstructor]
    public Bips(int value)
    {
        if (value is < Min or > Max)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, PaymentRejectionReason.BipsOutOfRange.ToToken());
        }

        Value = value;
    }

    [Id(0)]
    public int Value { get; }
}

public static class Commission
{
    public static readonly Bips PlatformDefault = new(2000);
}

[GenerateSerializer]
public sealed record CommissionBreakdown(
    [property: Id(0)] Money Gross,
    [property: Id(1)] Money PlatformFee,
    [property: Id(2)] Money SellerNet,
    [property: Id(3)] Bips PlatformFeeBips);

[GenerateSerializer]
public readonly record struct SaleId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct CheckoutSessionId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct PaymentEventId([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer]
public readonly record struct SellerPayoutAccountRef([property: Id(0)] string Value)
{
    public override string ToString() => Value;
}

[GenerateSerializer]
public enum SaleStatus
{
    Pending = 0,
    Paid = 1
}

[GenerateSerializer]
public enum PaymentEventType
{
    PaymentSucceeded = 0
}

[GenerateSerializer]
public enum PaymentRejectionReason
{
    None = 0,
    BipsOutOfRange = 1
}

public static class PaymentRejectionReasonTokens
{
    public const string BipsOutOfRange = "bips out of range";

    public static string ToToken(this PaymentRejectionReason reason) => reason switch
    {
        PaymentRejectionReason.BipsOutOfRange => BipsOutOfRange,
        _ => string.Empty
    };
}

[GenerateSerializer]
public sealed record Sale(
    [property: Id(0)] SaleId SaleId,
    [property: Id(1)] AccountId BuyerAccountId,
    [property: Id(2)] BundleId BundleId,
    [property: Id(3)] BundleVersion Version,
    [property: Id(4)] SellerPayoutAccountRef SellerPayoutAccount,
    [property: Id(5)] CommissionBreakdown Commission,
    [property: Id(6)] SaleStatus Status,
    [property: Id(7)] PaymentEventId? PaidEventId = null);

[GenerateSerializer]
public sealed record PaymentCheckoutRequest(
    [property: Id(0)] SaleId SaleId,
    [property: Id(1)] AccountId BuyerAccountId,
    [property: Id(2)] BundleId BundleId,
    [property: Id(3)] BundleVersion Version,
    [property: Id(4)] Money Gross,
    [property: Id(5)] SellerPayoutAccountRef SellerPayoutAccount,
    [property: Id(6)] Bips PlatformFeeBips);

[GenerateSerializer]
public sealed record PaymentCheckout(
    [property: Id(0)] CheckoutSessionId CheckoutSessionId,
    [property: Id(1)] Sale Sale,
    [property: Id(2)] Money ApplicationFee,
    [property: Id(3)] SellerPayoutAccountRef DestinationAccount);

[GenerateSerializer]
public sealed record PaymentWebhookEvent(
    [property: Id(0)] PaymentEventId EventId,
    [property: Id(1)] PaymentEventType EventType,
    [property: Id(2)] CheckoutSessionId CheckoutSessionId);

[GenerateSerializer]
public sealed record PaymentConfirmationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] Sale Sale,
    [property: Id(2)] EntitlementGranted? EntitlementAudit,
    [property: Id(3)] bool WasDuplicate);

[GenerateSerializer]
public sealed record BundlePaymentValidationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] PaymentRejectionReason RejectionReason,
    [property: Id(2)] IReadOnlyList<string> Diagnostics);

public interface IPaymentGateway
{
    Task<PaymentCheckout> CreateCheckoutAsync(
        PaymentCheckoutRequest request,
        CancellationToken cancellationToken = default);
}

public interface ISaleStore
{
    Task SavePendingAsync(
        PaymentCheckout checkout,
        CancellationToken cancellationToken = default);

    Task<PaymentCheckout?> FindCheckoutAsync(
        CheckoutSessionId checkoutSessionId,
        CancellationToken cancellationToken = default);

    Task<PaymentConfirmationResult?> FindConfirmationAsync(
        PaymentEventId eventId,
        CancellationToken cancellationToken = default);

    Task<PaymentConfirmationResult> MarkPaidAsync(
        PaymentWebhookEvent paymentEvent,
        EntitlementGranted entitlementAudit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Sale>> ListSalesAsync(CancellationToken cancellationToken = default);
}

public interface IPaymentProcessor
{
    Task<PaymentCheckout> OpenCheckoutAsync(
        PaymentCheckoutRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentConfirmationResult> ConfirmAsync(
        PaymentWebhookEvent paymentEvent,
        CancellationToken cancellationToken = default);
}

public interface IBundlePaymentPolicy
{
    BundlePaymentValidationResult ValidatePublishedBundle(PublishedBundleManifest manifest);
}
