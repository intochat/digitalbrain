using System.Diagnostics;
using MediatR;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Metrics;

namespace TripRadar.Server.API.Middlewares;

public class MetricBehavior<TRequest, TResponse>(CountMetric countMetric) : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IMonitoringService monitoredRequest) return await next.Invoke(cancellationToken);

        using var activity = CountMetric.ActivitySource.StartActivity("usecase", ActivityKind.Internal, default(ActivityContext));
        activity?.AddTag("trip-radar-server.command", request.GetType().Name);

        try
        {
            var result = await next.Invoke(cancellationToken);
            monitoredRequest.IncrementCount(countMetric);
            activity?.AddTag("error", false);

            return result;
        }
        catch (Exception)
        {
            monitoredRequest.DecrementCount(countMetric);
            activity?.AddTag("error", true);
            throw;
        }
    }
}
