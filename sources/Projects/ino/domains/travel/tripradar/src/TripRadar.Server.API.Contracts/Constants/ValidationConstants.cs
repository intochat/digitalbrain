namespace TripRadar.Server.API.Contracts.Constants;

public static class ValidationConstants
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 50;
    public const int MaxNameLength = 64;
    public const double MinLatitude = -90.0;
    public const double MaxLatitude = 90.0;
    public const double MinLongitude = -180.0;
    public const double MaxLongitude = 180.0;
}
