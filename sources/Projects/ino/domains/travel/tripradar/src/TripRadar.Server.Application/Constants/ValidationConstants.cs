namespace TripRadar.Server.Application.Constants;

public static class ValidationConstants
{
    public const int MaxUsernameLength = 50;
    public const int MaxFeedIdLength = 100;
    public const int MaxOperatorIdLength = 100;
    public const int MaxRouteIdLength = 100;
    public const int MaxServedByLength = 200;

    public const double MinLatitude = -90.0;
    public const double MaxLatitude = 90.0;
    public const double MinLongitude = -180.0;
    public const double MaxLongitude = 180.0;

    public const int MinRadius = 100;
    public const int MaxRadius = 10000;

    public const int MinLimit = 1;
    public const int MaxLimit = 100;
}
