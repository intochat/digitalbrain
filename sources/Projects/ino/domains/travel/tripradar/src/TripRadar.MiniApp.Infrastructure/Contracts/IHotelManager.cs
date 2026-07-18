using TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IHotelManager : IManager
{
    Task<HotelSearchResult?> SearchAsync(HotelSearchParams p);
    Task<HotelSearchResult?> LoadMoreAsync(HotelSearchParams p, string nextPageToken);
    Task<HotelProperty?> GetPropertyDetailsAsync(HotelSearchParams p, string propertyToken);
}