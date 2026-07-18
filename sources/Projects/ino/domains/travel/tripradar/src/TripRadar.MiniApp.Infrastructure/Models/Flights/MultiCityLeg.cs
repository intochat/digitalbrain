namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed class MultiCityLeg
{
    public string DepartureId { get; set; } = "";
    public string DepartureName { get; set; } = "";
    public string? DepartureCountryCode { get; set; }
    public string ArrivalId { get; set; } = "";
    public string ArrivalName { get; set; } = "";
    public string? ArrivalCountryCode { get; set; }
    public string Date { get; set; } = "";
}