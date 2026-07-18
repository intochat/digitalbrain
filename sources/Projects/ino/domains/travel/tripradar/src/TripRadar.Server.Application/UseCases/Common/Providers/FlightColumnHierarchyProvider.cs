using TripRadar.Server.Application.Constants.Flights;

namespace TripRadar.Server.Application.UseCases.Common.Providers;

public class FlightColumnHierarchyProvider : ColumnHierarchyProvider
{
    protected override Dictionary<string, string?> ColumnHierarchies => new()
    {
        // Root level properties
        { SavedFlightQueryColumnNameConstants.SearchMetadata, SavedFlightQueryColumnNameConstants.SearchMetadata },
        { SavedFlightQueryColumnNameConstants.SearchParameters, SavedFlightQueryColumnNameConstants.SearchParameters },
        {
            SavedFlightQueryColumnNameConstants.SearchInformation, SavedFlightQueryColumnNameConstants.SearchInformation
        },
        { SavedFlightQueryColumnNameConstants.BestFlights, SavedFlightQueryColumnNameConstants.BestFlights },
        { SavedFlightQueryColumnNameConstants.OtherFlights, SavedFlightQueryColumnNameConstants.OtherFlights },
        {
            SavedFlightQueryColumnNameConstants.SerpapiPagination, SavedFlightQueryColumnNameConstants.SerpapiPagination
        },

        // Search metadata properties
        { SavedFlightQueryColumnNameConstants.CreatedAt, SavedFlightQueryColumnNameConstants.SearchMetadata },
        { SavedFlightQueryColumnNameConstants.Status, SavedFlightQueryColumnNameConstants.SearchMetadata },
        { SavedFlightQueryColumnNameConstants.ProcessedAt, SavedFlightQueryColumnNameConstants.SearchMetadata },
        { SavedFlightQueryColumnNameConstants.TotalTimeTaken, SavedFlightQueryColumnNameConstants.SearchMetadata },

        // Search parameters properties
        { SavedFlightQueryColumnNameConstants.Engine, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.LanguageCode, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.CountryCode, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.Type, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.DepartureId, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.ArrivalId, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.OutboundDate, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.Adults, SavedFlightQueryColumnNameConstants.SearchParameters },
        { SavedFlightQueryColumnNameConstants.Currency, SavedFlightQueryColumnNameConstants.SearchParameters },

        // Best flights properties
        { SavedFlightQueryColumnNameConstants.Flights, SavedFlightQueryColumnNameConstants.BestFlights },
        { SavedFlightQueryColumnNameConstants.BestFlightsLayovers, SavedFlightQueryColumnNameConstants.BestFlights },
        { SavedFlightQueryColumnNameConstants.TotalDuration, SavedFlightQueryColumnNameConstants.BestFlights },
        {
            SavedFlightQueryColumnNameConstants.BestFlightsCarbonEmissions,
            SavedFlightQueryColumnNameConstants.BestFlights
        },
        { SavedFlightQueryColumnNameConstants.Price, SavedFlightQueryColumnNameConstants.BestFlights },
        { SavedFlightQueryColumnNameConstants.AirlineLogo, SavedFlightQueryColumnNameConstants.BestFlights },
        { SavedFlightQueryColumnNameConstants.BookingToken, SavedFlightQueryColumnNameConstants.BestFlights },

        // Flight properties
        { SavedFlightQueryColumnNameConstants.DepartureAirport, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.ArrivalAirport, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.Duration, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.Airplane, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.Airline, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.TravelClass, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.FlightNumber, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.TicketAlsoSoldBy, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.Legroom, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.Extensions, SavedFlightQueryColumnNameConstants.Flights },
        { SavedFlightQueryColumnNameConstants.PlaneAndCrewBy, SavedFlightQueryColumnNameConstants.Flights },

        // Airport properties
        { SavedFlightQueryColumnNameConstants.AirportName, SavedFlightQueryColumnNameConstants.Airport },
        { SavedFlightQueryColumnNameConstants.AirportId, SavedFlightQueryColumnNameConstants.Airport },
        { SavedFlightQueryColumnNameConstants.AirportTime, SavedFlightQueryColumnNameConstants.Airport },

        // Carbon emissions properties
        { SavedFlightQueryColumnNameConstants.ThisFlight, SavedFlightQueryColumnNameConstants.CarbonEmissions },
        {
            SavedFlightQueryColumnNameConstants.TypicalForThisRoute, SavedFlightQueryColumnNameConstants.CarbonEmissions
        },
        { SavedFlightQueryColumnNameConstants.DifferencePercent, SavedFlightQueryColumnNameConstants.CarbonEmissions },

        // Layover properties

        // Price insights properties
        { SavedFlightQueryColumnNameConstants.LowestPrice, SavedFlightQueryColumnNameConstants.PriceInsights },
        { SavedFlightQueryColumnNameConstants.PriceLevel, SavedFlightQueryColumnNameConstants.PriceInsights },
        { SavedFlightQueryColumnNameConstants.TypicalPriceRange, SavedFlightQueryColumnNameConstants.PriceInsights },
        { SavedFlightQueryColumnNameConstants.PriceHistory, SavedFlightQueryColumnNameConstants.PriceInsights }
    };

    protected override HashSet<string?> ValidColumns => [..ColumnHierarchies.Keys.Concat(ColumnHierarchies.Values)];
}
