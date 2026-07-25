using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Testing;

[SuppressMessage(
    "Performance",
    "CA1822:Mark members as static",
    Justification = "Instance API keeps TestBrain call sites stable while diagnostics stay no-op.")]
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

    internal void RecordOwner(string label, string ownerId)
    {
        _ = label;
        _ = ownerId;
    }

    internal void RecordEvent(
        string operation,
        string state,
        params (string Key, string Value)[] metadata)
    {
        _ = operation;
        _ = state;
        _ = metadata;
    }

    internal void SetClock(DateTimeOffset value) => _ = value;

    internal void TrackFault(JournalFaultHandle handle, string target)
    {
        _ = handle;
        _ = target;
    }

    internal void RetireFault(JournalFaultHandle handle, string state)
    {
        _ = handle;
        _ = state;
    }

    internal void RecordCleanupLeak(JournalFaultHandle handle) => _ = handle;

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
