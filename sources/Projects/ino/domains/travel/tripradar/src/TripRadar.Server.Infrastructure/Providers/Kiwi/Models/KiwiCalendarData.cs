using System.Text.Json.Serialization;

namespace TripRadar.Server.Infrastructure.Providers.Kiwi.Models
{
    internal sealed class KiwiCalendarData
    {
        [JsonPropertyName("itineraryPricesCalendar")]
        public KiwiItineraryPricesCalendar? ItineraryPricesCalendar { get; set; }
    }
}