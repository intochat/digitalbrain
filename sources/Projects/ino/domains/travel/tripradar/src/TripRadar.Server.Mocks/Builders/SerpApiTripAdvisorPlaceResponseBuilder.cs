using System.Text.Json;
using Microsoft.Extensions.Logging;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Application.DTO.Responses;

namespace TripRadar.Server.Mocks.Builders;

public class SerpApiTripAdvisorPlaceResponseBuilder
{
    private readonly ILogger<SerpApiTripAdvisorPlaceResponseBuilder> _logger;

    public SerpApiTripAdvisorPlaceResponseBuilder(ILogger<SerpApiTripAdvisorPlaceResponseBuilder> logger)
    {
        _logger = logger;
    }

    public GetTripAdvisorPlaceResponseDTO Build(string placeId, string? tripadvisorDomain)
    {
        _logger.LogInformation("Building TripAdvisor place mock response for place_id '{PlaceId}'", placeId);

        var placeResult = new Dictionary<string, object>
        {
            ["type"] = "restaurant",
            ["name"] = "Mock TripAdvisor Place",
            ["description"] = "A highly rated mock place for testing.",
            ["location_id"] = placeId,
            ["rating"] = 4.7,
            ["num_reviews"] = 256,
            ["address"] = "789 Broadway Ave",
            ["latitude"] = 40.7128,
            ["longitude"] = -74.0060,
            ["web_url"] = "https://www.tripadvisor.com/MockPlace",
            ["website"] = "https://example.com"
        };

        return new GetTripAdvisorPlaceResponseDTO
        {
            SearchMetadata = new SearchMetadata
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "Success",
                JsonEndpoint = "https://serpapi.com/search.json?engine=tripadvisor_place",
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ProcessedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                TotalTimeTaken = 1.1
            },
            SearchParameters = new TripAdvisorPlaceSearchParametersDTO
            {
                Engine = "tripadvisor_place",
                PlaceId = placeId,
                TripadvisorDomain = tripadvisorDomain ?? "tripadvisor.com"
            },
            SearchInformation = new SearchInformation { TotalResults = 1 },
            PlaceResult = placeResult
        };
    }

    public string BuildJson(string placeId, string? tripadvisorDomain)
    {
        var response = Build(placeId, tripadvisorDomain);
        return JsonSerializer.Serialize(response);
    }
}
