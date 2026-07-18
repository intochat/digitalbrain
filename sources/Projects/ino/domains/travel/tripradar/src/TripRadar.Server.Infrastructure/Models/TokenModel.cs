namespace TripRadar.Server.Infrastructure.Models;

public class TokenModel
{
    public required string UsernameOrEmail { get; set; }

    public required string Password { get; set; }
}
