namespace TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

public sealed class FlightSearchParams
{
    public string DepartureId { get; set; } = "";
    public string ArrivalId { get; set; } = "";
    public string DepartureName { get; set; } = "";
    public string ArrivalName { get; set; } = "";
    public string? DepartureCountryCode { get; set; }
    public string? ArrivalCountryCode { get; set; }
    public string OutboundDate { get; set; } = "";
    public string ReturnDate { get; set; } = "";
    public FlightType Type { get; set; } = FlightType.RoundTrip;
    public TravelClass TravelClass { get; set; } = TravelClass.Economy;
    public int Adults { get; set; } = 1;
    public int Children { get; set; }
    public int Infants { get; set; }

    public int TotalPassengers => Adults + Children + Infants;
}