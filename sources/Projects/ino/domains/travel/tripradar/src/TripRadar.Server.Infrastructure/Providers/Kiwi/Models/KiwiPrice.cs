using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Models
{
    internal sealed class KiwiPrice
    {
        [JsonPropertyName("amount")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? Amount { get; set; }
    }
}