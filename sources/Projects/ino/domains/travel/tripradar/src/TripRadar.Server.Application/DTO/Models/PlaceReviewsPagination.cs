using System.Text.Json.Serialization;
using TripRadar.Server.Comms.Core.Attributes;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Application.DTO.Models;

public class PlaceReviewsPagination
{
    [Preference(nameof(PreferenceType.Limit))]
    [JsonPropertyName("num")]
    public int? Num { get; set; }

    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}
