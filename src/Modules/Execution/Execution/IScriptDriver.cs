namespace DigitalBrain.Execution;

public interface IScriptDriver
{
    Task RunAsync(
        ExecutionSession session,
        WorkloadDescriptor workload,
        IReadOnlyList<CapabilityId> grants,
        CancellationToken cancellationToken);
}
