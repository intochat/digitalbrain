using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record ExploreAirport(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name
    );
}