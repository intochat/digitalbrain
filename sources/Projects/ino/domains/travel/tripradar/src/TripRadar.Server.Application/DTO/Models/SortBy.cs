using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public enum SortBy
{
    TopFlights = 1,
    Price = 2,
    DepartureTime = 3,
    ArrivalTime = 4,
    Duration = 5,
    Emissions = 6
}
