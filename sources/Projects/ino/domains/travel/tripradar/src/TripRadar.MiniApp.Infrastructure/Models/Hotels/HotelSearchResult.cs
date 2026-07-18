using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Hotels;

public sealed record HotelSearchResult(
    List<HotelProperty>? Properties,
    List<HotelBrand>? Brands,
    HotelSearchInfo? SearchInformation,
    [property: JsonPropertyName("serpapiPagination")] HotelPagination? Pagination
);