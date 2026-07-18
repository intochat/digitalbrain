using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class UpdatePromoCodeRequest
{
    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    [StringLength(500, ErrorMessage = "Description must not exceed 500 characters.")]
    public string? Description { get; set; }

    [JsonPropertyName("maxUsageCount")]
    [DataMember(Name = "maxUsageCount")]
    [Range(1, int.MaxValue, ErrorMessage = "Max usage count must be greater than 0.")]
    public int? MaxUsageCount { get; set; }

    [JsonPropertyName("maxUsagePerUser")]
    [DataMember(Name = "maxUsagePerUser")]
    [Range(1, int.MaxValue, ErrorMessage = "Max usage per user must be greater than 0.")]
    public int? MaxUsagePerUser { get; set; }

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    public bool? IsActive { get; set; }
}