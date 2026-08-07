namespace DigitalBrain;

/// <summary>
/// Signals that a behavior attempted to author a fact reserved for a different runtime authority.
/// </summary>
internal sealed class AuthoredSynapseRejectedException : InvalidOperationException
{
    public AuthoredSynapseRejectedException()
    {
    }

    public AuthoredSynapseRejectedException(string message)
        : base(message)
    {
    }

    public AuthoredSynapseRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
