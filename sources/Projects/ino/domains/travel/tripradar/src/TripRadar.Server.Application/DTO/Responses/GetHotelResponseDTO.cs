using System.Text.Json.Serialization;
using TripRadar.Server.Application.DTO.Models;

namespace TripRadar.Server.Application.DTO.Responses;

public class GetHotelResponseDTO
{
    [JsonPropertyName("search_metadata")]
    public SearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("search_parameters")]
    public HotelSearchParameters? SearchParameters { get; set; }

    [JsonPropertyName("search_information")]
    public SearchInformation? SearchInformation { get; set; }

    [JsonPropertyName("brands")]
    public List<Brand>? Brands { get; set; }

    [JsonPropertyName("properties")]
    public List<Property>? Properties { get; set; }

    [JsonPropertyName("serpapi_pagination")]
    public SerpapiPagination? SerpapiPagination { get; set; }
}
