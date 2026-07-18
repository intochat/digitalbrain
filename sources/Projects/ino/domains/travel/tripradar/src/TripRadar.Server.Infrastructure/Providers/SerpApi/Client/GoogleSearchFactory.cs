using System.Collections;
using SerpApi;
using TripRadar.Server.Infrastructure.Contracts;

namespace TripRadar.Server.Infrastructure.Providers.SerpApi.Client;

internal sealed class GoogleSearchFactory : IGoogleSearchFactory
{
    public string? ExecuteSearch(Hashtable parameters, string apiKey, int timeoutSeconds)
    {
        var search = new GoogleSearch(parameters, apiKey);
        search.setTimeoutSeconds(Math.Max(1, timeoutSeconds));
        var response = search.GetJson();
        return response.ToString();
    }
}
