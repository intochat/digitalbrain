namespace DigitalBrain.Testing;

internal sealed class BrainTestDiagnostics
{
    private readonly string _scopeId;

    internal BrainTestDiagnostics(
        string fixtureId,
        string scopeId,
        IEnumerable<string> moduleIds,
        DateTimeOffset clockOrigin)
    {
        _ = fixtureId;
        _scopeId = scopeId;
        _ = moduleIds;
        _ = clockOrigin;
    }

    internal BrainTestFailureException CaptureFailure(
        string operation,
        Exception failure,
        string? cleanupStage = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _ = cleanupStage;

        if (failure is BrainTestFailureException diagnostic)
        {
            return diagnostic;
        }

        return new BrainTestFailureException(
            $"DigitalBrain test framework operation '{operation}' failed (scope '{_scopeId}').",
            failure);
    }
}
