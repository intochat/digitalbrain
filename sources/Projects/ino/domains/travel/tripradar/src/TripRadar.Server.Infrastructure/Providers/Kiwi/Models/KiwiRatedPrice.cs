using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Models
{
    internal sealed class KiwiRatedPrice
    {
        [JsonPropertyName("price")]
        public KiwiPrice? Price { get; set; }

        [JsonPropertyName("rating")]
        public string? Rating { get; set; }
    }
}