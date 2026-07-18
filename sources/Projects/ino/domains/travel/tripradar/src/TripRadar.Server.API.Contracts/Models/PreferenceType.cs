using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public sealed class PreferenceType
{
    [JsonPropertyName("serviceTypeName")]
    public string ServiceTypeName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("dataType")]
    public string DataType { get; set; } = string.Empty;

    [JsonPropertyName("validationSchema")]
    public string? ValidationSchema { get; set; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
}
