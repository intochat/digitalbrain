using MediatR;
using TripRadar.Server.Application.Constants;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.ValueObjects;

namespace TripRadar.Server.Application.UseCases.SearchEngine.Hotels.Commands.CreateScheduledHotelQuery;

public record CreateScheduledHotelQueryCommand(
    string Location,
    string Username,
    DateTime CheckInDate,
    DateTime CheckOutDate,
    IList<QueryColumn>? SelectedColumns = null,
    string? AdditionalParametersJson = null,
    DateTime? NextExecutionTime = null,
    string? Schedule = null) : IRequest<Result<Guid>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledHotel, 1, CountMetric.SetResult(true));
    }

    public void DecrementCount(CountMetric countMetric)
    {
        countMetric.UpdateMetric(MetricConstants.CreateScheduledHotel, 1, CountMetric.SetResult(false));
    }
}
