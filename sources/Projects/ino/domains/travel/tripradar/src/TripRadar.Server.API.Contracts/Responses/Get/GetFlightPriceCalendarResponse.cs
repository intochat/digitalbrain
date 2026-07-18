namespace TripRadar.Server.API.Contracts.Responses.Get;

public class GetFlightPriceCalendarResponse
{
    public List<PriceCalendarDay> Days { get; set; } = [];
    public string? CheapestDate { get; set; }
    public decimal? CheapestPrice { get; set; }
}

public class PriceCalendarDay
{
    public string Date { get; set; } = "";
    public decimal? LowestPrice { get; set; }
    public string Currency { get; set; } = "USD";
}
