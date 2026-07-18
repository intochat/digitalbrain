using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Contracts;

public interface IPriceTrackingManager : IManager
{
    Task<List<ScheduledExecution>> GetAllAsync();
    Task<CreateScheduledQueryResponse?> TrackFlightAsync(CreateFlightTrackingRequest request);
    Task DeleteAsync(Guid uniqueId);
    Task ToggleAsync(Guid uniqueId, bool isActive, string schedule, DateTime nextExecution);
}