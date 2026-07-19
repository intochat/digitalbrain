namespace DigitalBrain.Testing;

public sealed class SimulationAssertionException : Exception
{
    public SimulationAssertionException()
        : this("A simulation expectation was not met.")
    {
    }

    public SimulationAssertionException(string message)
        : base(message)
    {
    }

    public SimulationAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
