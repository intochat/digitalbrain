namespace TripRadar.Server.Comms.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class ServiceProviderAttribute(string serviceName) : Attribute
{
    public string ServiceName { get; } = serviceName;
}
