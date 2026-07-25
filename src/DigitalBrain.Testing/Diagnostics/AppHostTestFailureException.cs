using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "AppHost test failures always wrap an operation description.")]
public sealed class AppHostTestFailureException : InvalidOperationException
{
    public AppHostTestFailureException(string message)
        : base(message)
    {
    }

    public AppHostTestFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
