using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Enums;

namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetPromoCodeResponse
{
    [JsonPropertyName("code")]
    [DataMember(Name = "code")]
    [Required]
    public string Code { get; set; } = null!;

    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    public string? Description { get; set; }

    [JsonPropertyName("discountType")]
    [DataMember(Name = "discountType")]
    [Required]
    public DiscountType DiscountType { get; set; }

    [JsonPropertyName("discountValue")]
    [DataMember(Name = "discountValue")]
    [Required]
    public decimal DiscountValue { get; set; }

    [JsonPropertyName("maxUsageCount")]
    [DataMember(Name = "maxUsageCount")]
    public int? MaxUsageCount { get; set; }

    [JsonPropertyName("currentUsageCount")]
    [DataMember(Name = "currentUsageCount")]
    [Required]
    public int CurrentUsageCount { get; set; }

    [JsonPropertyName("maxUsagePerUser")]
    [DataMember(Name = "maxUsagePerUser")]
    [Required]
    public int MaxUsagePerUser { get; set; }

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    [Required]
    public DateTime StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    [Required]
    public DateTime EndDate { get; set; }

    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    [Required]
    public bool IsActive { get; set; }

    [JsonPropertyName("isExpired")]
    [DataMember(Name = "isExpired")]
    [Required]
    public bool IsExpired { get; set; }

    [JsonPropertyName("createdAt")]
    [DataMember(Name = "createdAt")]
    [Required]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    [DataMember(Name = "updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
