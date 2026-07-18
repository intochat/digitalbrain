using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsFiltersDTO
{
    [Preference(nameof(PreferenceType.SortBy))]
    [JsonPropertyName("sort_by")]
    public string? SortBy { get; set; }

    [JsonPropertyName("topic_id")]
    public string? TopicId { get; set; }
}
