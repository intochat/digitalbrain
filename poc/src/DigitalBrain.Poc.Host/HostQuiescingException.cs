namespace DigitalBrain.Poc.Host;

public sealed class HostQuiescingException : Exception
{
    public HostQuiescingException()
        : base("The active host is quiescing; retry against the next active host.")
    {
    }
}
