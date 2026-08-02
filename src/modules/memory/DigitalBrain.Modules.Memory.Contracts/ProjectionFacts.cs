using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

public static class VectorProjectionKinds
{
    public const string Module = "module";
    public const string Neuron = "neuron";
    public const string Synapse = "synapse";
    public const string Behavior = "behavior";
}

public static class VectorProjectionMetadataKeys
{
    public const string Kind = "kind";
    public const string ContractId = "contract_id";
    public const string SchemaVersion = "schema_version";
    public const string ModuleId = "module_id";
    public const string NeuronContractId = "neuron_contract_id";
    public const string BehaviorId = "behavior_id";
    public const string ArtifactHash = "artifact_hash";
    public const string Visibility = "visibility";
}

public enum BehaviorProjectionVisibility
{
    Draft = 0,
    Private = 1,
    Stopped = 2,
    Published = 3,
}

[GenerateSerializer]
[Alias("memory.vector-projection-entry")]
public sealed record VectorProjectionEntry(
    [property: Id(0)] string Key,
    [property: Id(1)] string Text,
    [property: Id(2)] IReadOnlyDictionary<string, string> Metadata);

[GenerateSerializer]
[Alias("memory.behavior-projection-source")]
public sealed record BehaviorProjectionSource(
    [property: Id(0)] string BehaviorId,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] IReadOnlyList<string> ScenarioTitles,
    [property: Id(4)] string? ArtifactHash,
    [property: Id(5)] BehaviorProjectionVisibility Visibility);

[GenerateSerializer]
[Alias("memory.vector-projection-reconciled")]
[Description("Result of reconciling a reserved vector projection namespace")]
public sealed record VectorProjectionReconciled(
    [property: Id(0)] VectorMemoryNamespace Namespace,
    [property: Id(1)] int Upserted,
    [property: Id(2)] int Removed,
    [property: Id(3)] IReadOnlyList<string> ActiveKeys) : Synapse;
