namespace DigitalBrain.Core;

public enum TargetTier
{
    Run,
    Deploy
}

[GenerateSerializer]
[Alias("DigitalBrain.Core.GenerateCode")]
public record GenerateCode(
    [property: Id(0)] string Spec,
    [property: Id(1)] TargetTier Tier,
    [property: Id(2)] string Hints = "") : Synapse(nameof(GenerateCode), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CodeGenerated")]
public record CodeGenerated(
    [property: Id(0)] string Spec,
    [property: Id(1)] string Source,
    [property: Id(2)] TargetTier Tier,
    [property: Id(3)] IReadOnlyList<string> RequiredRefs) : Synapse(nameof(CodeGenerated), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.RunGeneratedCode")]
public record RunGeneratedCode(
    [property: Id(0)] string Source,
    [property: Id(1)] string Entrypoint = "Run",
    [property: Id(2)] IReadOnlyList<string>? Refs = null) : Synapse(nameof(RunGeneratedCode), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CodeRunResult")]
public record CodeRunResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Output,
    [property: Id(2)] string Error = "") : Synapse(nameof(CodeRunResult), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.DeployGeneratedCode")]
public record DeployGeneratedCode(
    [property: Id(0)] string Source,
    [property: Id(1)] string ModuleName,
    [property: Id(2)] IReadOnlyList<string>? Refs = null,
    [property: Id(3)] string CheckpointId = "") : Synapse(nameof(DeployGeneratedCode), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.CodeBuilt")]
public record CodeBuilt(
    [property: Id(0)] string ModuleName,
    [property: Id(1)] bool Success,
    [property: Id(2)] string BuildLog) : Synapse(nameof(CodeBuilt), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.KernelRestartRequested")]
public record KernelRestartRequested(
    [property: Id(0)] string Reason,
    [property: Id(1)] string ModuleName) : Synapse(nameof(KernelRestartRequested), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.FoundryRequest")]
public record FoundryRequest(
    [property: Id(0)] string Spec,
    [property: Id(1)] TargetTier Tier,
    [property: Id(2)] bool AutoApply = false) : Synapse(nameof(FoundryRequest), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.FoundryCheckpointed")]
public record FoundryCheckpointed(
    [property: Id(0)] string Spec,
    [property: Id(1)] string CheckpointId) : Synapse(nameof(FoundryCheckpointed), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.FoundryApplyStaged")]
public record FoundryApplyStaged(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string FoundryNeuronId,
    [property: Id(2)] string Spec,
    [property: Id(3)] TargetTier Tier,
    [property: Id(4)] string Source,
    [property: Id(5)] IReadOnlyList<string> RequiredRefs,
    [property: Id(6)] string CheckpointId,
    [property: Id(7)] string ModuleName = "") : Synapse(nameof(FoundryApplyStaged), DateTimeOffset.UtcNow);
[GenerateSerializer]
[Alias("DigitalBrain.Core.FoundryCompleted")]
public record FoundryCompleted(
    [property: Id(0)] string Spec,
    [property: Id(1)] TargetTier Tier,
    [property: Id(2)] string Outcome,
    [property: Id(3)] bool Applied) : Synapse(nameof(FoundryCompleted), DateTimeOffset.UtcNow);

[GenerateSerializer]
[Alias("DigitalBrain.Core.FoundryRolledBack")]
public record FoundryRolledBack(
    [property: Id(0)] string Spec,
    [property: Id(1)] string Reason,
    [property: Id(2)] string CheckpointId) : Synapse(nameof(FoundryRolledBack), DateTimeOffset.UtcNow);

[Alias("DigitalBrain.Core.ICodeGenNeuron")]
public interface ICodeGenNeuron : INeuron, IHandle<GenerateCode> { }

[Alias("DigitalBrain.Core.ICodeRunNeuron")]
public interface ICodeRunNeuron : INeuron, IHandle<RunGeneratedCode> { }

[Alias("DigitalBrain.Core.ICodeDeployNeuron")]
public interface ICodeDeployNeuron : INeuron, IHandle<DeployGeneratedCode> { }

[Alias("DigitalBrain.Core.ICodeFoundryLoopNeuron")]
public interface ICodeFoundryLoopNeuron : INeuron, IHandle<FoundryRequest> { }



