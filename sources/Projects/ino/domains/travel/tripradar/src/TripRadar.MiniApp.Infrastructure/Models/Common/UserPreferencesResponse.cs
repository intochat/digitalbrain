using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record UserPreferencesResponse(
        [property: JsonPropertyName("preferences")] List<UserPreferenceItem> Preferences
    );
}