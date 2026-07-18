using System.ComponentModel.DataAnnotations;

namespace TripRadar.Server.API.Contracts.Models;

public class MapsPagination
{
    [Range(0, 100, ErrorMessage = "Start must be between 0 and 100.")]
    public int? Start { get; set; }

    [Range(1, 20, ErrorMessage = "Num must be between 1 and 20.")]
    public int? Num { get; set; }
}
