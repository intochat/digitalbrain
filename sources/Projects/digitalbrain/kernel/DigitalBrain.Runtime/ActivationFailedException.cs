namespace DigitalBrain.Runtime;

public sealed class ActivationFailedException : Exception
{
    public ActivationFailedException(string message) : base(message)
    {
    }

    public ActivationFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
