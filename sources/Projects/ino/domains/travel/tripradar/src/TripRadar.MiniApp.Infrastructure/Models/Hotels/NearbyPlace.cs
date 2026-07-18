namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels
{
    public sealed record NearbyPlace(
        string? Name,
        List<Transportation>? Transportations
    );
}