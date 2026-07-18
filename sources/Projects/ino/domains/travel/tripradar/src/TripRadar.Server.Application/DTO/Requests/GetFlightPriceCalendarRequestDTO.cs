namespace TripRadar.Server.Application.DTO.Requests;

public class GetFlightPriceCalendarRequestDTO
{
    public required string DepartureId { get; set; }
    public required string ArrivalId { get; set; }
    public required int Year { get; set; }
    public required int Month { get; set; }
    public string? Gl { get; set; }
    public string? Hl { get; set; }
    public string? Currency { get; set; }
    public int? TripLengthDays { get; set; }
}
