namespace DigitalBrain.Poc.Runtime;

public sealed record AuthenticatedPrincipal
{
    public AuthenticatedPrincipal(string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        OwnerId = ownerId;
    }

    public string OwnerId { get; }
}
