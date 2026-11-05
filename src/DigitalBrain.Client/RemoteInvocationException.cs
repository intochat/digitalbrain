namespace DigitalBrain.Client;

/// <summary>
/// Thrown when a remote neuron call is refused or fails on the runtime edge.
/// </summary>
public class RemoteInvocationException : InvalidOperationException
{
    public RemoteInvocationException(string message) : base(message) { }

    public RemoteInvocationException(string message, string? code) : base(message)
    {
        Code = code;
    }

    public RemoteInvocationException(string message, Exception innerException) : base(message, innerException) { }

    public RemoteInvocationException(string message, string? code, Exception innerException) : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Optional refusal code reported by the server edge.
    /// </summary>
    public string? Code { get; }
}
