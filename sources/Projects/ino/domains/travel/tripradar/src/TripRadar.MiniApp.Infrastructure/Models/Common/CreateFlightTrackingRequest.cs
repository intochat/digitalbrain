using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record CreateFlightTrackingRequest(
        [property: JsonPropertyName("departureAirportCode")] string DepartureAirportCode,
        [property: JsonPropertyName("destinationAirportCode")] string DestinationAirportCode,
        [property: JsonPropertyName("departureDate")] DateTime DepartureDate,
        [property: JsonPropertyName("returnDate")] DateTime? ReturnDate,
        [property: JsonPropertyName("schedule")] string Schedule,
        [property: JsonPropertyName("nextExecutionTime")] DateTime NextExecutionTime
    );
}