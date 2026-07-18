using System.Collections;

namespace TripRadar.Server.Infrastructure.Contracts;

internal interface IGoogleSearchFactory
{
    string? ExecuteSearch(Hashtable parameters, string apiKey, int timeoutSeconds);
}