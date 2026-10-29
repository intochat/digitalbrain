namespace DigitalBrain.Invocation;

/// <summary>An invocation does not bind to its contract: unknown method, wrong arguments, ambiguity or oversized arguments.</summary>
public sealed class BindingException : Exception
{
    /// <summary>Creates an empty binding error.</summary>
    public BindingException() { }

    /// <summary>Creates a binding error with a message.</summary>
    public BindingException(string message) : base(message) { }

    /// <summary>Creates a binding error with a message and its cause.</summary>
    public BindingException(string message, Exception innerException) : base(message, innerException) { }
}
