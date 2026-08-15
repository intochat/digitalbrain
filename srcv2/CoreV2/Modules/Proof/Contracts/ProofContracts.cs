using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;

namespace Brain.Modules.Proof.Contracts;

public static class ProofContracts
{
    public static ModuleId Module { get; } = new("proof");

    public static NeuronRoleId EntryRole { get; } = new("proof.entry");
    public static NeuronRoleId CorrectionEntryRole { get; } = new("proof.correct");
    public static NeuronRoleId SourceRole { get; } = new("proof.source");
    public static NeuronRoleId SummaryRole { get; } = new("proof.summary");
    public static NeuronRoleId AssessmentRole { get; } = new("proof.assessment");

    public static ContractId Input { get; } = new("proof/input@1");
    public static ContractId Result { get; } = new("proof/result@1");
    public static ContractId CorrectionInput { get; } = new("proof/correction-input@1");
    public static ContractId CorrectionResult { get; } = new("proof/correction-result@1");
    public static ContractId Produced { get; } = new("proof/produced@1");
    public static ContractId Assessed { get; } = new("proof/assessed@1");
    public static ContractId CapabilityInput { get; } = new("proof/classifier-input@1");
    public static ContractId CapabilityResult { get; } = new("proof/classifier-result@1");

    public static CapabilityId Classifier { get; } = new("proof.classifier");

    public static OperationDescriptor Run { get; } = new(
        new OperationId("proof/run@1"), Input, Result, EntryRole, Module, new ContractVersion(1));

    public static OperationDescriptor Correct { get; } = new(
        new OperationId("proof/correct@1"), CorrectionInput, CorrectionResult, CorrectionEntryRole, Module, new ContractVersion(1));

    public static CapabilityDescriptor ClassifierCapability { get; } = new(
        Classifier, CapabilityInput, CapabilityResult, new ModuleId("proof.classifier"), new ContractVersion(1));
}
