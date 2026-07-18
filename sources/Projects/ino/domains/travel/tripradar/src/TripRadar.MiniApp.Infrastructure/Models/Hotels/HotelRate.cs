namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels
{
    public sealed record HotelRate(
        string? Lowest,
        decimal? ExtractedLowest,
        string? BeforeTaxesFees,
        decimal? ExtractedBeforeTaxesFees
    );
}