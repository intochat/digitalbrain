namespace DigitalBrain.Runtime.Orleans.Operations;

/// <summary>Optional host evidence about the exact process running an operation, independent of its network endpoint.</summary>
public interface IOperationDriverLifetime
{
    /// <summary>Captures an opaque process incarnation, or null when the host cannot identify it reliably.</summary>
    string? Capture();

    /// <summary>True proves that process ended; false proves it is alive; null leaves its lifetime unknown.</summary>
    bool? HasEnded(string token);
}
