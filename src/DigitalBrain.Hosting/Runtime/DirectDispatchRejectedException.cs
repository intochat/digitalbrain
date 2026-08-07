namespace DigitalBrain;

internal sealed class DirectDispatchRejectedException : InvalidOperationException
{
    public DirectDispatchRejectedException()
    {
    }

    public DirectDispatchRejectedException(string message)
        : base(message)
    {
    }

    public DirectDispatchRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
