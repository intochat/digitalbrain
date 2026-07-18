using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record UserPreferenceItem(
        [property: JsonPropertyName("preferenceTypeDisplayName")] string PreferenceTypeDisplayName,
        [property: JsonPropertyName("value")] string Value
    );
}