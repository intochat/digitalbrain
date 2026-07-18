using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Responses.Get;

/// <summary>
/// Response containing user's payment methods for the billing page.
/// </summary>
public class GetPaymentMethodsResponse
{
    /// <summary>
    /// List of payment methods associated with the user.
    /// </summary>
    [JsonPropertyName("paymentMethods")]
    [DataMember(Name = "paymentMethods")]
    public List<PaymentMethodDto> PaymentMethods { get; set; } = [];

    /// <summary>
    /// Whether the user has an active subscription.
    /// </summary>
    [JsonPropertyName("hasActiveSubscription")]
    [DataMember(Name = "hasActiveSubscription")]
    public bool HasActiveSubscription { get; set; }
}

/// <summary>
/// Payment method details.
/// </summary>
public class PaymentMethodDto
{
    /// <summary>
    /// Stripe payment method ID.
    /// </summary>
    [JsonPropertyName("id")]
    [DataMember(Name = "id")]
    public string Id { get; set; } = null!;

    /// <summary>
    /// Type of payment method (currently only "card").
    /// </summary>
    [JsonPropertyName("type")]
    [DataMember(Name = "type")]
    public string Type { get; set; } = "card";

    /// <summary>
    /// Card details.
    /// </summary>
    [JsonPropertyName("card")]
    [DataMember(Name = "card")]
    public CardDetailsDto Card { get; set; } = null!;

    /// <summary>
    /// Billing details associated with the payment method.
    /// </summary>
    [JsonPropertyName("billingDetails")]
    [DataMember(Name = "billingDetails")]
    public BillingDetailsDto? BillingDetails { get; set; }

    /// <summary>
    /// Whether this is the default payment method for the subscription.
    /// </summary>
    [JsonPropertyName("isDefault")]
    [DataMember(Name = "isDefault")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// Date when the payment method was added.
    /// </summary>
    [JsonPropertyName("createdAt")]
    [DataMember(Name = "createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Card details for a payment method.
/// </summary>
public class CardDetailsDto
{
    /// <summary>
    /// Card brand (visa, mastercard, amex, discover, diners, jcb, unionpay).
    /// </summary>
    [JsonPropertyName("brand")]
    [DataMember(Name = "brand")]
    public string Brand { get; set; } = null!;

    /// <summary>
    /// Last 4 digits of card number.
    /// </summary>
    [JsonPropertyName("last4")]
    [DataMember(Name = "last4")]
    public string Last4 { get; set; } = null!;

    /// <summary>
    /// Expiration month (1-12).
    /// </summary>
    [JsonPropertyName("expMonth")]
    [DataMember(Name = "expMonth")]
    public int ExpMonth { get; set; }

    /// <summary>
    /// Expiration year (e.g., 2024).
    /// </summary>
    [JsonPropertyName("expYear")]
    [DataMember(Name = "expYear")]
    public int ExpYear { get; set; }

    /// <summary>
    /// Country of card issuance.
    /// </summary>
    [JsonPropertyName("country")]
    [DataMember(Name = "country")]
    public string? Country { get; set; }
}

/// <summary>
/// Billing details for a payment method.
/// </summary>
public class BillingDetailsDto
{
    /// <summary>
    /// Name on the card.
    /// </summary>
    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Email associated with the billing.
    /// </summary>
    [JsonPropertyName("email")]
    [DataMember(Name = "email")]
    public string? Email { get; set; }

    /// <summary>
    /// Billing address.
    /// </summary>
    [JsonPropertyName("address")]
    [DataMember(Name = "address")]
    public BillingAddressDto? Address { get; set; }
}

/// <summary>
/// Billing address for a payment method.
/// </summary>
public class BillingAddressDto
{
    /// <summary>
    /// Country code.
    /// </summary>
    [JsonPropertyName("country")]
    [DataMember(Name = "country")]
    public string? Country { get; set; }

    /// <summary>
    /// Postal code.
    /// </summary>
    [JsonPropertyName("postalCode")]
    [DataMember(Name = "postalCode")]
    public string? PostalCode { get; set; }
}
