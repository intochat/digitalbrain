using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Get;

public class ValidatePromoCodeRequest
{
    [JsonPropertyName("code")]
    [DataMember(Name = "code")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Promo code is required.")]
    [StringLength(50, ErrorMessage = "Promo code must not exceed 50 characters.")]
    public string Code { get; set; } = null!;
}