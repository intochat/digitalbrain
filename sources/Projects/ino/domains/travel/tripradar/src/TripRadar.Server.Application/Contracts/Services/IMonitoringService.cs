using TripRadar.Server.Application.Metrics;

namespace TripRadar.Server.Application.Contracts.Services;

public interface IMonitoringService
{
    void IncrementCount(CountMetric countMetric);

    void DecrementCount(CountMetric countMetric);
}
