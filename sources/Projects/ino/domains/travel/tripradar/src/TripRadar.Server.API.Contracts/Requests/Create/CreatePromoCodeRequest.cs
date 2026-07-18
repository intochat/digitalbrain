using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreatePromoCodeRequest
{
    [JsonPropertyName("code")]
    [DataMember(Name = "code")]
    [Required(AllowEmptyStrings = false, ErrorMessage = "Promo code is required.")]
    [StringLength(50, ErrorMessage = "Promo code must not exceed 50 characters.")]
    [RegularExpression(@"^[A-Z0-9]+$", ErrorMessage = "Promo code can only contain uppercase letters and numbers.")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; set; }

    [JsonPropertyName("discountType")]
    [DataMember(Name = "discountType")]
    [Required(ErrorMessage = "Discount type is required.")]
    public DiscountType DiscountType { get; set; }

    [JsonPropertyName("discountValue")]
    [DataMember(Name = "discountValue")]
    [Required(ErrorMessage = "Discount value is required.")]
    public decimal DiscountValue { get; set; }

    [JsonPropertyName("maxUsageCount")]
    [DataMember(Name = "maxUsageCount")]
    [Range(1, int.MaxValue, ErrorMessage = "Max usage count must be greater than 0.")]
    public int? MaxUsageCount { get; set; }

    [JsonPropertyName("maxUsagePerUser")]
    [DataMember(Name = "maxUsagePerUser")]
    [Required(ErrorMessage = "Max usage per user is required.")]
    [Range(1, int.MaxValue, ErrorMessage = "Max usage per user must be greater than 0.")]
    public int MaxUsagePerUser { get; set; }

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    [Required(ErrorMessage = "Start date is required.")]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    [Required(ErrorMessage = "End date is required.")]
    public DateTime EndDate { get; set; }

    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    public bool IsActive { get; set; }
}
