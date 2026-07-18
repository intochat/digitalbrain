namespace TripRadar.MiniApp.Client.Infrastructure.Routes;

public static class ApiEndpoints
{
    public const string GraphQl = "/graphql";
    public const string AirportsSearch = "/api/v1/search/airports";
    public const string UserProfile = "/api/v1/users/profile";
    public const string UserPortableSession = "/api/v1/users/portable-session";
    public const string UserPreferences = "/api/v1/preferences/user";
    public const string ScheduledExecutions = "/api/v1/scheduled-executions";
    public const string FlightScheduledQueries = "/api/v1/scheduled-queries/flights";

    public static string AirportsSearchFor(string encodedQuery, string languageCode) => $"{AirportsSearch}?query={encodedQuery}&hl={languageCode}";

    public static string ScheduledExecution(Guid uniqueId) => $"{ScheduledExecutions}/{uniqueId}";

    public static string ScheduledExecutionConfiguration(Guid uniqueId) => $"{ScheduledExecution(uniqueId)}/configuration";
}