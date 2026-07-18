using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetPromoCodeUsageResponse
{
    [JsonPropertyName("promoCode")]
    [DataMember(Name = "promoCode")]
    [Required]
    public string PromoCode { get; set; } = null!;

    [JsonPropertyName("username")]
    [DataMember(Name = "username")]
    public string? Username { get; set; }

    [JsonPropertyName("usedAt")]
    [DataMember(Name = "usedAt")]
    [Required]
    public DateTime UsedAt { get; set; }

    [JsonPropertyName("discountApplied")]
    [DataMember(Name = "discountApplied")]
    [Required]
    public decimal DiscountApplied { get; set; }
}
