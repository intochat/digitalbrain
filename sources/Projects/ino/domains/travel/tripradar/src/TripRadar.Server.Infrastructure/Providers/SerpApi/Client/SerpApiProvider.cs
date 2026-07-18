using System.Collections;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Providers;
using TripRadar.Server.Comms.Core.Exceptions;
using TripRadar.Server.Infrastructure.Providers.SerpApi.Settings;

namespace TripRadar.Server.Infrastructure.Providers.SerpApi.Client;

public class SerpApiProvider(HttpClient httpClient, IOptions<SerpApiSettings> serpApiSettings) : ISerpApiProvider
{
    private const string ZeroTraceParameterName = "zero_trace";

    private readonly SerpApiSettings _serpApiSettings = serpApiSettings.Value;

    public async Task<string?> FindAsync(Hashtable parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestUri = BuildRequestUri(parameters);

            using var response = await httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(payload) ? null : payload;
            }

            throw CreateInternalErrorException(response.StatusCode, payload);
        }
        catch (InternalErrorException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw CreateInternalErrorException(exception);
        }
    }

    private string BuildRequestUri(Hashtable parameters)
    {
        var query = RemoveZeroTraceParameter(parameters)
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Value is not null)
            .ToDictionary(
                entry => Convert.ToString(entry.Key, CultureInfo.InvariantCulture)!,
                entry => Convert.ToString(entry.Value, CultureInfo.InvariantCulture)!,
                StringComparer.OrdinalIgnoreCase);

        query["api_key"] = _serpApiSettings.ApiKey;
        query.TryAdd("output", "json");

        return QueryHelpers.AddQueryString(_serpApiSettings.SearchEndpoint, query);
    }

    private static Hashtable RemoveZeroTraceParameter(Hashtable parameters)
    {
        var sanitizedParameters = new Hashtable();
        foreach (var entry in parameters.Cast<DictionaryEntry>()
                     .Where(entry => !string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), ZeroTraceParameterName, StringComparison.OrdinalIgnoreCase)))
        {
            sanitizedParameters[entry.Key] = entry.Value;
        }

        return sanitizedParameters;
    }

    private static InternalErrorException CreateInternalErrorException(HttpRequestException exception)
    {
        var errorMessage = exception.Message.ToLowerInvariant() switch
        {
            { } msg when msg.Contains("timed out") => "The SerpApi request timed out.",
            { } msg when msg.Contains("name or service not known") || msg.Contains("no such host") => "SerpApi host could not be resolved.",
            _ => "The SerpApi request failed."
        };

        return new InternalErrorException($"{errorMessage} Details: {exception.Message}", exception);
    }

    private static InternalErrorException CreateInternalErrorException(HttpStatusCode statusCode, string? payload)
    {
        var details = string.IsNullOrWhiteSpace(payload) ? statusCode.ToString() : payload;
        var errorMessage = statusCode switch
        {
            HttpStatusCode.BadRequest => "Invalid SerpApi request parameters.",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "SerpApi authentication failed.",
            HttpStatusCode.NotFound => "SerpApi did not return results for the requested resource.",
            HttpStatusCode.TooManyRequests => "SerpApi rate limit was exceeded.",
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "The SerpApi request timed out.",
            _ => "The SerpApi request failed."
        };

        return new InternalErrorException($"{errorMessage} Details: {details}");
    }
}
