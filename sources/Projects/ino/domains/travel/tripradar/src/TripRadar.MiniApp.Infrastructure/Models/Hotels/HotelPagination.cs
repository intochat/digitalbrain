namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels
{
    public sealed record HotelPagination(
        int? CurrentFrom,
        int? CurrentTo,
        string? NextPageToken
    );
}