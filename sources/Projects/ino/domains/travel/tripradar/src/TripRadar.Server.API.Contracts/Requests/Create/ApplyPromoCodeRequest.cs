using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class ApplyPromoCodeRequest
{
    [JsonPropertyName("code")]
    [DataMember(Name = "code")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Promo code is required.")]
    [StringLength(50, ErrorMessage = "Promo code must not exceed 50 characters.")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("orderAmount")]
    [DataMember(Name = "orderAmount")]
    [Required(ErrorMessage = "Order amount is required.")]
    [Range(0.01, double.MaxValue, ErrorMessage = "Order amount must be greater than 0.")]
    public decimal OrderAmount { get; set; }
}