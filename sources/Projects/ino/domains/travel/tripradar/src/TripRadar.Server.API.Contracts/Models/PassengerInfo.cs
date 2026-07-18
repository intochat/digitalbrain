using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class PassengerInfo
{
    [Range(1, 10, ErrorMessage = "At least one adult is required.")]
    public int? Adults { get; set; }

    [Range(0, 10, ErrorMessage = "The number of children must be between 0 and 10.")]
    public int? Children { get; set; }

    [Range(0, 10, ErrorMessage = "The number of infants in seat must be between 0 and 10.")]
    public int? InfantsInSeat { get; set; }

    [Range(0, 10, ErrorMessage = "The number of infants on lap must be between 0 and 10.")]
    public int? InfantsOnLap { get; set; }
}
