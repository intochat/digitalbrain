using System.Text.Json;
using Polly.Timeout;
using TripRadar.Server.Application.ApplicationErrors;
using TripRadar.Server.Comms.Core.Errors;

namespace TripRadar.Server.Infrastructure.Services.Providers.SerpApi;

internal static class SerpApiErrorMapper
{
    public static Error Map(Exception exception) => exception switch
    {
        JsonException jsonException => Errors.SerpApiDeserializationFailed with { Reason = jsonException.Message },
        TimeoutRejectedException => Errors.SerpApiRequestFailed with { Reason = "Request timed out" },
        HttpRequestException httpException => Errors.SerpApiRequestFailed with { Reason = httpException.Message },
        _ => Errors.SerpApiRequestFailed with { Reason = exception.Message }
    };
}
