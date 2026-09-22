using DigitalBrain.Coding;

namespace DigitalBrain.Behavior;

public sealed record BehaviorLaunch(string ProgramId, Guid GenerationId, VerifiedArtifact Artifact,
    string ConfigurationJson, Func<bool, Task> Readiness, Func<string, string, Task> Log);
public sealed record BehaviorExit(int ExitCode, string? Error);
public sealed record BehaviorExecution(Guid GenerationId, int ProcessId, Task<BehaviorExit> Completion);

public interface IBehaviorExecutor
{
    Task<BehaviorExecution> StartAsync(BehaviorLaunch launch, CancellationToken cancellationToken);
    Task StopAsync(Guid generationId, CancellationToken cancellationToken);
}
