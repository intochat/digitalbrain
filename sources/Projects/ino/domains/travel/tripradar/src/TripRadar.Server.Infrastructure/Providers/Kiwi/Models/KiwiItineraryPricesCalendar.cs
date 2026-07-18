using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Models
{
    internal sealed class KiwiItineraryPricesCalendar
    {
        [JsonPropertyName("__typename")]
        public string? TypeName { get; set; }

        [JsonPropertyName("calendar")]
        public IReadOnlyCollection<KiwiPricePerDateItem>? Calendar { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}