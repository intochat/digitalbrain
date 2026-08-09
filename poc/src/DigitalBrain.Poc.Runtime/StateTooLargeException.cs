namespace DigitalBrain.Poc.Runtime;

public sealed class StateTooLargeException(int actualBytes, int maximumBytes) : Exception(
    $"Serialized state is {actualBytes} bytes; the configured maximum is {maximumBytes} bytes.")
{
    public int ActualBytes { get; } = actualBytes;

    public int MaximumBytes { get; } = maximumBytes;
}
