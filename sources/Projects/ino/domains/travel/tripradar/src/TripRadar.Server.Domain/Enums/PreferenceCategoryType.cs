using TripRadar.Server.Domain.SeedWork;

namespace TripRadar.Server.Domain.Enums;

public class PreferenceCategoryType(int id, string name) : Enumeration(id, name)
{
    public static readonly PreferenceCategoryType Travel = new(1, "Travel");
    public static readonly PreferenceCategoryType LocalServices = new(2, "Local Services");
    public static readonly PreferenceCategoryType Dining = new(3, "Dining");
    public static readonly PreferenceCategoryType Content = new(4, "Content");
    public static readonly PreferenceCategoryType Utilities = new(5, "Utilities");

    public static IReadOnlyList<PreferenceCategoryType> GetAllCategories() =>
    [
        Travel,
        LocalServices,
        Dining,
        Content,
        Utilities
    ];

    public static IReadOnlyList<PreferenceCategoryType> GetActiveCategories() =>
        GetAllCategories()
            .Where(category => GetActiveCategoryIds().Contains(category.Id))
            .ToList();

    public static PreferenceCategoryType GetById(int id) =>
        GetAllCategories().Single(category => category.Id == id);

    public static PreferenceCategoryType GetByServiceType(ServiceType serviceType)
    {
        if (Equals(serviceType, ServiceType.Event) ||
            Equals(serviceType, ServiceType.Flight) ||
            Equals(serviceType, ServiceType.Hotel) ||
            Equals(serviceType, ServiceType.FlightExplore) ||
            Equals(serviceType, ServiceType.FlightPriceCalendar) ||
            Equals(serviceType, ServiceType.TripAdvisorSearch) ||
            Equals(serviceType, ServiceType.TripAdvisorPlace))
        {
            return Travel;
        }

        if (Equals(serviceType, ServiceType.LocalPlaces) ||
            Equals(serviceType, ServiceType.Maps) ||
            Equals(serviceType, ServiceType.PlaceReview) ||
            Equals(serviceType, ServiceType.YelpSearch) ||
            Equals(serviceType, ServiceType.YelpPlace) ||
            Equals(serviceType, ServiceType.YelpReviews) ||
            Equals(serviceType, ServiceType.MapsDirections) ||
            Equals(serviceType, ServiceType.MapsPlaceResults))
        {
            return LocalServices;
        }

        if (Equals(serviceType, ServiceType.OpenTableReview) ||
            Equals(serviceType, ServiceType.YelpPlaceFullMenu))
        {
            return Dining;
        }

        if (Equals(serviceType, ServiceType.YouTubeSearch))
        {
            return Content;
        }

        if (Equals(serviceType, ServiceType.GoogleLightSearch))
        {
            return Utilities;
        }

        throw new ArgumentOutOfRangeException(nameof(serviceType), serviceType.Name, "Unsupported service type for preference category.");
    }

    private static IReadOnlyCollection<int> GetActiveCategoryIds() =>
        ServiceType.GetActivePreferenceServices()
            .Select(serviceType => GetByServiceType(serviceType).Id)
            .Distinct()
            .ToArray();
}
