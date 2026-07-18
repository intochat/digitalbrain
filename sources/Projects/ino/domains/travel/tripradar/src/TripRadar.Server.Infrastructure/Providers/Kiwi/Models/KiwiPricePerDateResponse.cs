using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Models;

internal sealed class KiwiPricePerDateResponse
{
    [JsonPropertyName("data")]
    public KiwiCalendarData? Data { get; set; }
}