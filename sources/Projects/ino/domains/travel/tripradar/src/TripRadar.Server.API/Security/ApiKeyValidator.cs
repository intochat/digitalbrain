using TripRadar.Server.API.Contracts;
using TripRadar.Server.Comms.Core.Helpers;

namespace TripRadar.Server.API.Security;

internal sealed class ApiKeyValidator(IConfiguration configuration) : IApiKeyValidator
{
    public bool IsValid(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var configuredApiKey = configuration.GetValue<string>("ApiKey");
        return !string.IsNullOrWhiteSpace(configuredApiKey) && ComparerHelper.Compare(configuredApiKey, apiKey);
    }
}
