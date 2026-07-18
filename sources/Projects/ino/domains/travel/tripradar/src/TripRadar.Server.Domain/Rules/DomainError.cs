namespace TripRadar.Server.Domain.Rules;

public sealed record DomainError(string Code, string Reason)
{
    public static readonly DomainError None = new(string.Empty, string.Empty);
}
