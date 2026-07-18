namespace TripRadar.Server.Application.DTO.Responses;

public class GetFlightPriceCalendarResponseDTO
{
    public List<PriceCalendarDayDTO> Days { get; set; } = [];
    public string? CheapestDate { get; set; }
    public decimal? CheapestPrice { get; set; }
}

public class PriceCalendarDayDTO
{
    public string Date { get; set; } = "";
    public decimal? LowestPrice { get; set; }
    public string Currency { get; set; } = "USD";
}
