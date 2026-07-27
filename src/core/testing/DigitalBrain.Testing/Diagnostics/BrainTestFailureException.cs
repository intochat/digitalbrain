using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Framework failures always wrap the original exception.")]
public sealed class BrainTestFailureException : Exception
{
    public BrainTestFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
