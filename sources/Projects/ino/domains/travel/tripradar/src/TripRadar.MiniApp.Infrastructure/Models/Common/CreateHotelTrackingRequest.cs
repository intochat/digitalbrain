using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record CreateHotelTrackingRequest(
        [property: JsonPropertyName("location")] string Location,
        [property: JsonPropertyName("checkInDate")] DateTime CheckInDate,
        [property: JsonPropertyName("checkOutDate")] DateTime CheckOutDate,
        [property: JsonPropertyName("schedule")] string Schedule,
        [property: JsonPropertyName("nextExecutionTime")] DateTime NextExecutionTime
    );
}