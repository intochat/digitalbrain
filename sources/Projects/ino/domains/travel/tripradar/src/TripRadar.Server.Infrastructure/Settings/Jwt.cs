namespace TripRadar.Server.Infrastructure.Settings;

public class Jwt
{
    public string Key { get; set; } = null!;

    public string? RefreshTokenKey { get; set; }

    public string Issuer { get; set; } = null!;

    public string Audience { get; set; } = null!;

    public int DurationInMonths { get; set; }

    public int DurationInMinutes { get; set; }

    public string Algorithm { get; set; } = null!;
}
