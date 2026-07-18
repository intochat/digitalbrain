using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiTripAdvisorSearchResponseBuilder
{
    private readonly ILogger<SerpApiTripAdvisorSearchResponseBuilder> _logger;

    public SerpApiTripAdvisorSearchResponseBuilder(ILogger<SerpApiTripAdvisorSearchResponseBuilder> logger)
    {
        _logger = logger;
    }

    public GetTripAdvisorSearchResponseDTO Build(
        string query,
        string? tripadvisorDomain,
        string? ssrc,
        int? offset,
        int? limit,
        double? lat,
        double? lon)
    {
        _logger.LogInformation("Building TripAdvisor search mock response for query '{Query}'", query);

        var searchParameters = new TripAdvisorSearchParametersDTO
        {
            Engine = "tripadvisor",
            Q = query,
            TripadvisorDomain = tripadvisorDomain ?? "tripadvisor.com",
            Ssrc = ssrc,
            Offset = offset,
            Limit = limit,
            Lat = lat,
            Lon = lon
        };

        var places = new List<Dictionary<string, object>>
        {
            new()
            {
                ["type"] = "restaurant",
                ["name"] = $"{query} Bistro",
                ["description"] = "Popular spot with excellent reviews.",
                ["rating"] = 4.6,
                ["reviews"] = "124",
                ["location_id"] = "123456",
                ["address"] = "123 Main St",
                ["phone"] = "+1 555-0100",
                ["website"] = "https://example.com"
            },
            new()
            {
                ["type"] = "attraction",
                ["name"] = $"{query} Museum",
                ["description"] = "Top-rated attraction.",
                ["rating"] = 4.8,
                ["reviews"] = "892",
                ["location_id"] = "654321",
                ["address"] = "456 Market St"
            }
        };

        var forums = new List<Dictionary<string, object>>
        {
            new()
            {
                ["title"] = $"Visiting {query} - tips?",
                ["link"] = "https://www.tripadvisor.com/Forum",
                ["snippet"] = "Looking for recommendations and tips."
            }
        };

        var locations = new List<Dictionary<string, object>>
        {
            new()
            {
                ["name"] = $"{query}",
                ["location_id"] = "999999",
                ["type"] = "destination"
            }
        };

        var serpapiPagination = new Dictionary<string, object>
        {
            ["next"] = $"https://serpapi.com/search.json?engine=tripadvisor&q={Uri.EscapeDataString(query)}&offset=20"
        };

        return new GetTripAdvisorSearchResponseDTO
        {
            SearchMetadata = new SearchMetadata
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "Success",
                JsonEndpoint = "https://serpapi.com/search.json?engine=tripadvisor",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ProcessedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                TotalTimeTaken = 1.2
            },
            SearchParameters = searchParameters,
            SearchInformation = new SearchInformation { TotalResults = places.Count },
            Places = places,
            Forums = forums,
            Locations = locations,
            SerpapiPagination = serpapiPagination
        };
    }

    public string BuildJson(
        string query,
        string? tripadvisorDomain,
        string? ssrc,
        int? offset,
        int? limit,
        double? lat,
        double? lon)
    {
        var response = Build(query, tripadvisorDomain, ssrc, offset, limit, lat, lon);
        return JsonSerializer.Serialize(response);
    }
}
