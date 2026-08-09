namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed class ProbeFailureException(string message) : Exception(message)
{
    public ProbeFailureException()
        : this("The fixed test handler failed after staging state and output.")
    {
    }
}
