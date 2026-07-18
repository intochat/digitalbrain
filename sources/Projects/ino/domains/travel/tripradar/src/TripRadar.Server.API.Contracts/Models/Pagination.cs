using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class Pagination
{
    [Range(0, int.MaxValue, ErrorMessage = "Start index must be non-negative.")]
    public int? Start { get; set; }
}
