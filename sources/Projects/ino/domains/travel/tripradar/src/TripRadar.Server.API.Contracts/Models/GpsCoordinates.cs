using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TripRadar.Server.API.Contracts.Constants;

namespace TripRadar.Server.API.Contracts.Models;

public class GpsCoordinates
{
    [JsonPropertyName("latitude")]
    [Range(ValidationConstants.MinLatitude, ValidationConstants.MaxLatitude,
        ErrorMessage = "Latitude must be between -90 and 90 degrees")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    [Range(ValidationConstants.MinLongitude, ValidationConstants.MaxLongitude,
        ErrorMessage = "Longitude must be between -180 and 180 degrees")]
    public double Longitude { get; set; }
}
