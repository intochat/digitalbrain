using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetOpenTableReviewsResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata SearchMetadata { get; set; } = new();

    [JsonPropertyName("search_parameters")]
    public OpenTableSearchParametersDTO SearchParameters { get; set; } = new();

    [JsonPropertyName("search_information")]
    public OpenTableSearchInformationDTO? SearchInformation { get; set; }

    [JsonPropertyName("reviews_summary")]
    public OpenTableReviewsSummaryDTO? ReviewsSummary { get; set; }

    [JsonPropertyName("awards")]
    public List<OpenTableAwardDTO>? Awards { get; set; }

    [JsonPropertyName("reviews")]
    public List<OpenTableReviewDTO>? Reviews { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public OpenTableSerpApiPaginationDTO? SerpApiPagination { get; set; }

    [JsonPropertyName("error")] public string? Error { get; set; }
}
