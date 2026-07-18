using System.Text.Json;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class HotelRecentSearchPayloadBuilder : IRecentSearchPayloadBuilder
{
    public ServiceType ServiceType => ServiceType.Hotel;

    public Task<IReadOnlyList<RecentSearchItemDetails>> BuildManyAsync(
        IReadOnlyList<TripQueryHistory> items,
        CancellationToken cancellationToken = default)
    {
        var results = new List<RecentSearchItemDetails>(items.Count);

        foreach (var item in items)
        {
            if (!TryBuild(item, out var builtItem))
            {
                continue;
            }

            results.Add(builtItem);
        }

        return Task.FromResult<IReadOnlyList<RecentSearchItemDetails>>(results);
    }

    private static bool TryBuild(TripQueryHistory item, out RecentSearchItemDetails result)
    {
        result = null!;

        try
        {
            using var document = JsonDocument.Parse(item.QueryParametersJson);
            var root = document.RootElement;
            var payload = new HotelRecentSearchPayloadDetails
            {
                Location = RecentSearchJsonReader.ReadString(root, "searchQuery", "q"),
                CheckInDate = RecentSearchJsonReader.ReadDate(root, "advancedParameters", "checkInDate"),
                CheckOutDate = RecentSearchJsonReader.ReadDate(root, "advancedParameters", "checkOutDate"),
                Adults = RecentSearchJsonReader.ReadInt(root, "advancedParameters", "adults"),
                Children = RecentSearchJsonReader.ReadInt(root, "advancedParameters", "children"),
                SortBy = RecentSearchJsonReader.ReadEnumName<HotelSortByType>(root, "filters", "sortBy"),
                MaxPrice = RecentSearchJsonReader.ReadInt(root, "filters", "maxPrice")
            };

            if (string.IsNullOrWhiteSpace(payload.Location) && !payload.CheckInDate.HasValue && !payload.CheckOutDate.HasValue)
            {
                return false;
            }

            result = new RecentSearchItemDetails
            {
                UniqueId = item.UniqueId,
                ServiceType = ServiceType.Hotel,
                CreatedOn = item.CreatedOn,
                Payload = payload
            };

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
