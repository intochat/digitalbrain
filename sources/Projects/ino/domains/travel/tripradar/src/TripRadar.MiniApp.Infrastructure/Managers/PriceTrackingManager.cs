using TripRadar.MiniApp.Client.Infrastructure.Contracts;
using TripRadar.MiniApp.Client.Infrastructure.Models.Common;

namespace TripRadar.MiniApp.Client.Infrastructure.Managers;

public sealed class PriceTrackingManager(TripRadarApiClient client) : IPriceTrackingManager
{
    public async Task<List<ScheduledExecution>> GetAllAsync()
    {
        var response = await client.GetAsync<ScheduledExecutionsResponse>(ApiEndpoints.ScheduledExecutions);
        return response?.ScheduledExecutions ?? [];
    }

    public Task<CreateScheduledQueryResponse?> TrackFlightAsync(CreateFlightTrackingRequest request) => client.PostAsync<CreateScheduledQueryResponse>(ApiEndpoints.FlightScheduledQueries, request);

    public async Task DeleteAsync(Guid uniqueId) => await client.DeleteAsync(ApiEndpoints.ScheduledExecution(uniqueId));

    public async Task ToggleAsync(Guid uniqueId, bool isActive, string schedule, DateTime nextExecution) => await client.PatchAsync(ApiEndpoints.ScheduledExecutionConfiguration(uniqueId), new UpdateScheduledConfigRequest(isActive, schedule, nextExecution));
}