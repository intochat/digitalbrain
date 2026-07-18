using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Create;

public class CreateSubscriptionCheckoutResponse
{
    [JsonPropertyName("clientSecret")]
    [DataMember(Name = "clientSecret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("currency")]
    [DataMember(Name = "currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("amountSubtotal")]
    [DataMember(Name = "amountSubtotal")]
    public decimal AmountSubtotal { get; set; }

    [JsonPropertyName("amountDiscount")]
    [DataMember(Name = "amountDiscount")]
    public decimal AmountDiscount { get; set; }

    [JsonPropertyName("amountTotal")]
    [DataMember(Name = "amountTotal")]
    public decimal AmountTotal { get; set; }

    [JsonPropertyName("promoCode")]
    [DataMember(Name = "promoCode")]
    public string? PromoCode { get; set; }
}
