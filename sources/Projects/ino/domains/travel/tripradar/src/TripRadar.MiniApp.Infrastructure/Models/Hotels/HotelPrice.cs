namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels
{
    public sealed record HotelPrice(
        string? Source,
        string? Logo,
        int? NumGuests,
        HotelRate? RatePerNight,
        bool? FreeCancellation
    );
}