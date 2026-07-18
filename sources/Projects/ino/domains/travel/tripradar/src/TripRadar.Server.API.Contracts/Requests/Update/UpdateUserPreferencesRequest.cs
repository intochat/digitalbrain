using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Models;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public sealed class UpdateUserPreferencesRequest : IValidatableObject
{
    [JsonPropertyName("preferences")]
    [DataMember(Name = "preferences")]
    [Required(ErrorMessage = "Preferences is required.")]
    public UserPreferences Preferences { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Preferences.Flight is null &&
            Preferences.Hotel is null &&
            Preferences.Event is null &&
            Preferences.LocalPlaces is null &&
            Preferences.Maps is null &&
            Preferences.PlaceReview is null &&
            Preferences.TripAdvisorSearch is null)
        {
            yield return new ValidationResult("At least one preference type must be provided.", [nameof(Preferences)]);
        }
    }
}
