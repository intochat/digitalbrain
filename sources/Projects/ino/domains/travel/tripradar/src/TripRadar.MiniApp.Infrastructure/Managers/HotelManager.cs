using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class HotelManager(TripRadarApiClient client) : IHotelManager
{
    public async Task<HotelSearchResult?> SearchAsync(HotelSearchParams p)
    {
        var request = BuildRequest(p);

        var wrapper = await client.GraphQlAsync<HotelsWrapper>(GraphQlQueries.SearchHotels, new { request });
        return wrapper?.Hotels;
    }

    public async Task<HotelSearchResult?> LoadMoreAsync(HotelSearchParams p, string nextPageToken)
    {
        var request = BuildRequest(p, tokenPagination: new PaginationInput(nextPageToken));

        var wrapper = await client.GraphQlAsync<HotelsWrapper>(GraphQlQueries.SearchHotels, new { request });
        return wrapper?.Hotels;
    }

    public async Task<HotelProperty?> GetPropertyDetailsAsync(HotelSearchParams p, string propertyToken)
    {
        var request = BuildRequest(p, booking: new BookingInput(propertyToken));

        var wrapper = await client.GraphQlAsync<HotelsWrapper>(GraphQlQueries.SearchHotels, new { request });
        return wrapper?.Hotels?.Properties?.FirstOrDefault();
    }

    private static object BuildRequest(
        HotelSearchParams p,
        PaginationInput? tokenPagination = null,
        BookingInput? booking = null)
    {
        return new
        {
            searchQuery = new { q = p.Query },
            advancedParameters = new
            {
                checkInDate = p.CheckInDate,
                checkOutDate = p.CheckOutDate,
                adults = p.Adults,
                children = p.Children,
                childrenAges = p.ChildrenAges.Count > 0 ? p.ChildrenAges : null
            },
            filters = new
            {
                sortBy = p.SortBy switch
                {
                    HotelSortBy.LowestPrice => "LowestPrice",
                    HotelSortBy.HighestPrice => "HighestPrice",
                    HotelSortBy.Rating => "HighestRating",
                    _ => null
                },
                minPrice = p.MinPrice,
                maxPrice = p.MaxPrice,
                freeCancellation = p.FreeCancellation ? true : (bool?)null
            },
            localization = new { gl = "us", hl = "en", currency = "USD" },
            tokenPagination,
            booking
        };
    }

    private sealed record HotelsWrapper(HotelSearchResult Hotels);
    private sealed record PaginationInput(string NextPageToken);
    private sealed record BookingInput(string PropertyToken);
}