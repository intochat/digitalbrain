namespace TripRadar.Server.API.Contracts;

internal interface IApiKeyValidator
{
    bool IsValid(string? apiKey);
}
