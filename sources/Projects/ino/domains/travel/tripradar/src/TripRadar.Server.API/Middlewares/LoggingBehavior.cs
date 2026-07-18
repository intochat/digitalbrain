using System.Diagnostics;
using MediatR;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.API.Middlewares;

public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestType = typeof(TRequest).Name;
        var responseType = typeof(TResponse).Name;
        var startedAt = Stopwatch.GetTimestamp();

        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["Operation"] = requestType,
                   ["RequestType"] = requestType,
                   ["ResponseType"] = responseType
               }))
        {
            logger.LogInformation("Handling {RequestType}", requestType);

            try
            {
                var response = await next(cancellationToken);
                var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

                if (TryGetResultError(response, out var error))
                {
                    logger.LogWarning(
                        "Handled {RequestType} with failure {ErrorCode} ({ErrorReason}) in {ElapsedMs} ms",
                        requestType,
                        error.Code,
                        error.Reason,
                        elapsedMs);
                }
                else
                {
                    logger.LogInformation(
                        "Handled {RequestType} with {ResponseType} in {ElapsedMs} ms",
                        requestType,
                        responseType,
                        elapsedMs);
                }

                return response;
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    exception,
                    "Request {RequestType} was cancelled after {ElapsedMs} ms",
                    requestType,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Unhandled exception in {RequestType} after {ElapsedMs} ms",
                    requestType,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                throw;
            }
        }
    }

    private static bool TryGetResultError(TResponse response, out Comms.Core.Errors.Error error)
    {
        if (response is Result { IsFailure: true } failedResult)
        {
            error = failedResult.Error;
            return true;
        }

        error = Comms.Core.Errors.Error.None;
        return false;
    }
}
