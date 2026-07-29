namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

public interface IBehaviorExecutor
{
    ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record BehaviorExecutionRequest(
    BehaviorExecutionMetadata Metadata,
    ReadOnlyMemory<byte> ArtifactBytes,
    string ArtifactHash,
    string TriggerTypeName,
    string TriggerJson,
    IBehaviorCapabilityResolver Capabilities,
    TimeProvider Time);

public sealed record BehaviorExecutionOutcome(bool Succeeded, string Outcome);

public interface IBehaviorCapabilityResolver
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get matches IBehaviorContext.Get for resolving an approved module neuron.")]
    TContract Get<TContract>(string name)
        where TContract : class, INeuron;
}

public interface IBehaviorInstallTests
{
    ValueTask<BehaviorInstallTestReport> RunAsync(
        IBehaviorContext context,
        IReadOnlyDictionary<string, string> features,
        CancellationToken cancellationToken);
}

public sealed record BehaviorInstallTestReport(
    bool Passed,
    int ScenarioCount,
    string Detail)
{
    public static BehaviorInstallTestReport Pass(int scenarioCount, string detail = "passed")
        => new(true, scenarioCount, detail);

    public static BehaviorInstallTestReport Fail(string detail, int scenarioCount = 0)
        => new(false, scenarioCount, detail);
}
