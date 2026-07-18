namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

public sealed class HotelSearchParams
{
    public string Query { get; set; } = "";
    public string CheckInDate { get; set; } = "";
    public string CheckOutDate { get; set; } = "";
    public int Adults { get; set; } = 2;
    public int Children { get; set; }
    public List<int> ChildrenAges { get; set; } = [];
    public HotelSortBy SortBy { get; set; } = HotelSortBy.Relevance;
    public int? MinPrice { get; set; }
    public int? MaxPrice { get; set; }
    public int? MinRating { get; set; }
    public bool FreeCancellation { get; set; }

    public int TotalGuests => Adults + Children;
}