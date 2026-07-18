using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights
{
    public sealed record PriceCalendarDay(
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("lowestPrice")] decimal? LowestPrice,
        [property: JsonPropertyName("currency")] string Currency
    );
}