using System.Text.Json.Serialization;
using System.Collections;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Requests;

public class GetPlaceReviewsRequestDTO : SerpApiBaseRequest, ISerpApiRequest
{
    [JsonPropertyName("placeId")]
    public string? PlaceId { get; set; }

    [JsonPropertyName("dataId")]
    public string? DataId { get; set; }

    [JsonPropertyName("localization")]
    public Localization? Localization { get; set; }

    [JsonPropertyName("filters")]
    public PlaceReviewsFiltersDTO? Filters { get; set; }

    [JsonPropertyName("pagination")]
    public PlaceReviewsPagination? Pagination { get; set; }

    public Hashtable GetQueryParams()
    {
        var ht = new Hashtable();

        AddIfNotNull("engine", "google_maps_reviews", ht);

        if (!string.IsNullOrEmpty(PlaceId))
        {
            AddIfNotNull("place_id", PlaceId, ht);
        }
        else if (!string.IsNullOrEmpty(DataId))
        {
            AddIfNotNull("data_id", DataId, ht);
        }

        AddIfNotNull("hl", Localization?.Hl, ht);

        if (Filters != null)
        {
            AddIfNotNull("sort_by", Filters.SortBy, ht);
            AddIfNotNull("topic_id", Filters.TopicId, ht);
        }

        if (Pagination != null)
        {
            AddIfNotNull("num", Pagination.Num, ht);
            AddIfNotNull("next_page_token", Pagination.NextPageToken, ht);
        }

        return ht;
    }
}

