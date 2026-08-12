namespace DigitalBrain.Auth;

public sealed class DigitalBrainUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string UserName { get; set; } = "";

    public string NormalizedUserName { get; set; } = "";

    public string? PasswordHash { get; set; }

    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public Guid PrincipalId { get; set; }

    public bool IsBootstrapOwner { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
