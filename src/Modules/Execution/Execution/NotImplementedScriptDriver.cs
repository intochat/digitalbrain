namespace DigitalBrain.Execution;

public sealed class NotImplementedScriptDriver : IScriptDriver
{
    public Task RunAsync(
        ExecutionSession session,
        WorkloadDescriptor workload,
        IReadOnlyList<CapabilityId> grants,
        CancellationToken cancellationToken)
    {
        _ = session;
        _ = workload;
        _ = grants;
        _ = cancellationToken;
        throw new NotImplementedException(
            "Out-of-process DigitalBrain.Scripting host is not wired yet. " +
            "Set DigitalBrain:Mode=Testing or DigitalBrain:Fakes:Enabled for the in-process allow-listed Script seam.");
    }
}
