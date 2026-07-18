using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Models;

public class QueryColumn
{
    [JsonPropertyName("name")]
    [DataMember(Name = "name")]
    [Required]
    public string Name { get; set; } = null!;

    [JsonPropertyName("isActive")]
    [DataMember(Name = "isActive")]
    [Required]
    public bool IsActive { get; set; }
}
