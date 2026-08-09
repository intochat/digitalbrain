namespace DigitalBrain.Poc.Host;

public sealed record OwnerSession(string OwnerId, string Token)
{
    public string OpaqueToken => Token;
}
