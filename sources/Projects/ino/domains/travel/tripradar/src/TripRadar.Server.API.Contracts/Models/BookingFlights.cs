using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class BookingFlights
{
    [Required] public required string BookingToken { get; set; }
}
