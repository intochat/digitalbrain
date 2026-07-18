namespace TripRadar.Server.Infrastructure.Providers.SerpApi.Settings;

public class SerpApiSettings
{
    public string ApiKey { get; set; } = null!;
    public int RequestTimeoutSeconds { get; set; } = 150;
    public string SearchEndpoint { get; set; } = "https://serpapi.com/search";
}
