namespace DigitalBrain.Core;

[GenerateSerializer]
[Alias("DigitalBrain.Core.SynapseType")]
public readonly record struct SynapseType([property: Id(0)] string Value);

[GenerateSerializer]
[Alias("DigitalBrain.Core.Synapse")]
public record Synapse(
    [property: Id(0)] string Type,
    [property: Id(1)] DateTimeOffset Timestamp,
    [property: Id(2)] NeuronId? Sender = null,
    [property: Id(3)] NeuronId? Receiver = null,
    [property: Id(4)] bool IsBroadcast = false,
    [property: Id(5)] string? CorrelationId = null
)
{
    [Id(6)] public string SynapseId { get; init; } = Guid.NewGuid().ToString("N");

    [Id(7)] public string? CausationId { get; init; }

    public Synapse Stamp(NeuronId sender, Synapse? cause = null) =>
        this with
        {
            Sender = sender,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = CorrelationId ?? cause?.CorrelationId ?? cause?.SynapseId ?? SynapseId,
            CausationId = cause?.SynapseId
        };
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.NeuronTelemetry")]
public record NeuronTelemetry(NeuronId Neuron, string Event, int Count = 1) : Synapse(nameof(NeuronTelemetry), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.WiringOptimizationProposed")]
public record WiringOptimizationProposed(string Proposal, string FromNeuron) : Synapse(nameof(WiringOptimizationProposed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ExperienceUsed")]
public record ExperienceUsed(
    string Pack,
    string Action,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(ExperienceUsed), DateTimeOffset.UtcNow);


[Alias("DigitalBrain.Core.IAspireNeuron")]
public interface IAspireNeuron : INeuron, IHandle<StartDistributedApp>, IHandle<RestartResource> { }

// Thin common marker for channel neurons.
// Allows discovery and shared patterns (e.g. CorrelationId/CausationId for reply context across channels).
// No methods yet - keeps it thin; specific contracts live in feature contract assemblies, not Core.
[Alias("DigitalBrain.Core.IChannelNeuron")]
public interface IChannelNeuron : INeuron
{
}

// IUser contract lives in Core so kernel can run standalone for security/air-gapped scenarios.
// Full identity and billing systems stay outside Core.
[GenerateSerializer]
[Alias("DigitalBrain.Core.UserId")]
public readonly record struct UserId([property: Id(0)] string Value)
{
    public static UserId Anonymous => new("anonymous");
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.CapabilityRegistered")]
public record CapabilityRegistered(
    string Id,
    string Description,
    IReadOnlyList<string> Examples,
    string Tier,
    string? Origin = null) : Synapse(nameof(CapabilityRegistered), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.IMetaOptimizerNeuron")]
public interface IMetaOptimizerNeuron : INeuron, IHandle<NeuronTelemetry>, IHandle<WiringOptimizationProposed> { }

[Alias("DigitalBrain.Core.IGeneratedNeuron")]
public interface IGeneratedNeuron : INeuron { }

[GenerateSerializer]
[Alias("DigitalBrain.Core.LlmPrompt")]
public record LlmPrompt(string Prompt, string? PreferredModel = null) : Synapse(nameof(LlmPrompt), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.LlmResponse")]
public record LlmResponse(string Prompt, string Response, string ModelUsed) : Synapse(nameof(LlmResponse), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.ILlmNeuron")]
public interface ILlmNeuron : INeuron, IHandle<LlmPrompt> { }



// Self-awareness: SystemStatus + proposals (MVP for auto diagnose + simulate fix)
[GenerateSerializer]
[Alias("DigitalBrain.Core.SystemStatusChanged")]
public record SystemStatusChanged(string Component, string Status, string? Details = null) : Synapse(nameof(SystemStatusChanged), DateTimeOffset.UtcNow);

// Dual journal checkpoints + branching for simulation / time travel.
[GenerateSerializer]
[Alias("DigitalBrain.Core.Checkpoint")]
public record Checkpoint(NeuronId Source, IReadOnlyList<Synapse> Snapshot, DateTimeOffset TakenAt) : Synapse(nameof(Checkpoint), TakenAt);

[GenerateSerializer]
[Alias("DigitalBrain.Core.BranchCreated")]
public record BranchCreated(NeuronId Source, string BranchId) : Synapse(nameof(BranchCreated), DateTimeOffset.UtcNow);

// Task protocol messages (recoverable task lifecycle for INO, MCP actions, orchestration).
// The durable grain impl (IKernelTask) lives in the kernel layer; these messages are universal core protocol.
[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskCreated")]
public record TaskCreated(TaskId TaskId, string Description) : Synapse(nameof(TaskCreated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskStarted")]
public record TaskStarted(TaskId TaskId) : Synapse(nameof(TaskStarted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskProgress")]
public record TaskProgress(TaskId TaskId, string Detail) : Synapse(nameof(TaskProgress), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskCompleted")]
public record TaskCompleted(TaskId TaskId, string? Result = null) : Synapse(nameof(TaskCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskCancelled")]
public record TaskCancelled(TaskId TaskId) : Synapse(nameof(TaskCancelled), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.RunTask")]
public record RunTask(
    TaskId TaskId,
    string Description,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(RunTask), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CancelTask")]
public record CancelTask(
    TaskId TaskId,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(CancelTask), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.TaskInfo")]
public record TaskInfo(
    [property: Id(0)] TaskId TaskId,
    [property: Id(1)] string Status,
    [property: Id(2)] string? Result = null
);

// 3D graph / cluster observation synapses
[GenerateSerializer]
[Alias("DigitalBrain.Core.ClusterActivity")]
public record ClusterActivity(string NodeId, string Activity, double Value) : Synapse(nameof(ClusterActivity), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.ThreeDGraphUpdate")]
public record ThreeDGraphUpdate(string GraphId, string DataJson) : Synapse(nameof(ThreeDGraphUpdate), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.VisualizeDataRequest")]
public record VisualizeDataRequest(
    string Prompt,
    string DataJson,
    string? ChartHint = null,
    string? RequestId = null,
    string UserId = "anonymous",
    string? SessionId = null) : Synapse(nameof(VisualizeDataRequest), DateTimeOffset.UtcNow, CorrelationId: RequestId);

[GenerateSerializer]
[Alias("DigitalBrain.Core.PerformKernelSelfUpdate")]
public record PerformKernelSelfUpdate(string Version = "", int FailAtReplica = 0) : Synapse(nameof(PerformKernelSelfUpdate), DateTimeOffset.UtcNow);

