using TripRadar.MiniApp.Client.Infrastructure.Models.Flights;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IPriceCalendarManager : IManager
{
    Task<PriceCalendarResult?> GetPriceCalendarAsync(string departureId, string arrivalId, int year, int month, int? tripLengthDays = null);
}