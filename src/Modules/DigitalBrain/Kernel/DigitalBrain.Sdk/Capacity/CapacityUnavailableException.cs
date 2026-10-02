namespace DigitalBrain.Sdk.Capacity;

[GenerateSerializer]
public sealed class CapacityUnavailableException : Exception
{
    public const string RefusalMessage = "Sorry, runtime provisioning is not accessible at the moment.";

    public CapacityUnavailableException() : base(RefusalMessage) { }
}
