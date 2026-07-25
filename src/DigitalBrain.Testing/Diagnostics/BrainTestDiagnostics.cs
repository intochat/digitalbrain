namespace DigitalBrain.Testing;

internal sealed class BrainTestDiagnostics
{
    private readonly string _scopeId;

    internal BrainTestDiagnostics(string scopeId)
        => _scopeId = scopeId;

    internal BrainTestFailureException CaptureFailure(
        string operation,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (failure is BrainTestFailureException diagnostic)
        {
            return diagnostic;
        }

        return new BrainTestFailureException(
            $"DigitalBrain test framework operation '{operation}' failed (scope '{_scopeId}').",
            failure);
    }
}
