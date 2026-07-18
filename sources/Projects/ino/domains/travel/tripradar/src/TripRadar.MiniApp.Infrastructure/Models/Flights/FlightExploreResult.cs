using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed record FlightExploreResult(
    [property: JsonPropertyName("destinations")] List<ExploreDestination> Destinations
);