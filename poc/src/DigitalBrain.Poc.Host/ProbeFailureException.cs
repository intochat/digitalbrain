namespace DigitalBrain.Poc.Host;

internal sealed class ProbeFailureException : Exception
{
    public ProbeFailureException()
        : base("The fixed test handler failed after staging state and output.")
    {
    }
}
