namespace DigitalBrain.Runtime.User;

public sealed class UserContext : IUser
{
    public IDomainDiscovery GetDomains { get; } = new DomainDiscovery();
}
