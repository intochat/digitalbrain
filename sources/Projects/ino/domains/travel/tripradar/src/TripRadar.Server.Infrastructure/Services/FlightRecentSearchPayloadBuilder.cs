using System.Text.Json;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Repositories.Models;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.DTO.Enums;
using TripRadar.Server.Application.DTO.Models;
using TripRadar.Server.Domain.Entities;
using TripRadar.Server.Domain.Enums;

namespace TripRadar.Server.Infrastructure.Services;

public sealed class FlightRecentSearchPayloadBuilder(IUnitOfWork unitOfWork) : IRecentSearchPayloadBuilder
{
    public ServiceType ServiceType => ServiceType.Flight;

    public async Task<IReadOnlyList<RecentSearchItemDetails>> BuildManyAsync(
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

        if (results.Count == 0)
        {
            return results;
        }

        var airportCodes = results
            .SelectMany(item => new[]
            {
                ((FlightRecentSearchPayloadDetails)item.Payload).DepartureAirportCode,
                ((FlightRecentSearchPayloadDetails)item.Payload).DestinationAirportCode
            })
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (airportCodes.Length == 0)
        {
            return results;
        }

        var airports = await unitOfWork.AirportRepository.GetByCodesAsync(airportCodes.Cast<string>(), cancellationToken);
        var cityByCode = airports
            .GroupBy(airport => airport.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().City, StringComparer.OrdinalIgnoreCase);

        foreach (var item in results)
        {
            var payload = (FlightRecentSearchPayloadDetails)item.Payload;

            if (string.IsNullOrWhiteSpace(payload.DepartureAirportCity)
                && !string.IsNullOrWhiteSpace(payload.DepartureAirportCode)
                && cityByCode.TryGetValue(payload.DepartureAirportCode, out var departureCity))
            {
                payload.DepartureAirportCity = departureCity;
            }

            if (string.IsNullOrWhiteSpace(payload.DestinationAirportCity)
                && !string.IsNullOrWhiteSpace(payload.DestinationAirportCode)
                && cityByCode.TryGetValue(payload.DestinationAirportCode, out var destinationCity))
            {
                payload.DestinationAirportCity = destinationCity;
            }
        }

        return results;
    }

    private static bool TryBuild(TripQueryHistory item, out RecentSearchItemDetails result)
    {
        result = null!;

        try
        {
            using var document = JsonDocument.Parse(item.QueryParametersJson);
            var root = document.RootElement;
            var payload = new FlightRecentSearchPayloadDetails
            {
                DepartureAirportCode = RecentSearchJsonReader.ReadString(root, "flightSearch", "departureId"),
                DestinationAirportCode = RecentSearchJsonReader.ReadString(root, "flightSearch", "arrivalId"),
                DepartureDate = RecentSearchJsonReader.ReadDate(root, "advancedOptions", "outboundDate"),
                ReturnDate = RecentSearchJsonReader.ReadDate(root, "advancedOptions", "returnDate"),
                Adults = RecentSearchJsonReader.ReadInt(root, "passengers", "adults"),
                Children = RecentSearchJsonReader.ReadInt(root, "passengers", "children"),
                TravelClass = RecentSearchJsonReader.ReadEnumName<TravelClassType>(root, "advancedOptions", "travelClass"),
                SortBy = RecentSearchJsonReader.ReadEnumName<SortBy>(root, "sorting", "sortBy"),
                MaxPrice = RecentSearchJsonReader.ReadInt(root, "filters", "maxPrice"),
                Stops = RecentSearchJsonReader.ReadInt(root, "filters", "stops"),
                IncludeAirlines = RecentSearchJsonReader.ReadCsvList(root, "filters", "includeAirlines"),
                Bags = RecentSearchJsonReader.ReadInt(root, "filters", "bags"),
                OutboundTimes = RecentSearchJsonReader.ReadString(root, "filters", "outboundTimes"),
                ReturnTimes = RecentSearchJsonReader.ReadString(root, "filters", "returnTimes"),
                EmissionsOnly = RecentSearchJsonReader.ReadBool(root, "filters", "emissions")
            };

            if (string.IsNullOrWhiteSpace(payload.DepartureAirportCode)
                && string.IsNullOrWhiteSpace(payload.DestinationAirportCode)
                && !payload.DepartureDate.HasValue
                && !payload.ReturnDate.HasValue)
            {
                return false;
            }

            result = new RecentSearchItemDetails
            {
                UniqueId = item.UniqueId,
                ServiceType = ServiceType.Flight,
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


