namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels
{
    public sealed record HotelBrand(
        int Id,
        string? Name,
        List<HotelBrandChild>? Children
    );
}