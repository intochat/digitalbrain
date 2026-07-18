using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Create;

public class CreateTripVaultRequest
{
    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    [Required(AllowEmptyStrings = false)]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    [DataMember(Name = "description")]
    [StringLength(2000)]
    public string? Description { get; set; }

    [JsonPropertyName("startDate")]
    [DataMember(Name = "startDate")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    [DataMember(Name = "endDate")]
    public DateTime? EndDate { get; set; }
}
