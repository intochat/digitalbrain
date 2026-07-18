using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class SearchQuery
{
    [Required] public string Q { get; set; } = null!; // Query
}
