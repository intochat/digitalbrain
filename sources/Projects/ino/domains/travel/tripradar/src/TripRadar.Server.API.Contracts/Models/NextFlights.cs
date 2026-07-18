using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class NextFlights
{
    [Required] public required string DepartureToken { get; set; }
}
