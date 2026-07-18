using MediatR;
using TripRadar.Server.Application.Contracts.Requests;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Domain.Entities;

namespace TripRadar.Server.Application.UseCases.Feedbacks.Queries.GetUserFeedback;

public record GetUserFeedbackQuery(string Username) : IRequest<Result<IEnumerable<Feedback>>>, IMonitoringService, IAuthorizedRequest
{
    public void IncrementCount(CountMetric countMetric)
    {
    }

    public void DecrementCount(CountMetric countMetric)
    {
    }
}
