using Brain.Abstractions.Events;
using Brain.Abstractions.Modules;
using Brain.Modules.Proof.Contracts;

namespace Brain.Modules.Proof;

public static class ProofManifest
{
    public static ModuleManifest Create() => new(
        ProofContracts.Module,
        new ModuleVersion(1, 0, 0),
        [],
        [
            new NeuronRoleDescriptor(ProofContracts.EntryRole, NeuronScope.Principal, ProofContracts.Module),
            new NeuronRoleDescriptor(ProofContracts.CorrectionEntryRole, NeuronScope.Principal, ProofContracts.Module),
            new NeuronRoleDescriptor(ProofContracts.SourceRole, NeuronScope.Principal, ProofContracts.Module),
            new NeuronRoleDescriptor(ProofContracts.SummaryRole, NeuronScope.Workspace, ProofContracts.Module),
            new NeuronRoleDescriptor(ProofContracts.AssessmentRole, NeuronScope.Workspace, ProofContracts.Module),
        ],
        [ProofContracts.Run, ProofContracts.Correct],
        [
            new EventDescriptor(ProofContracts.Produced, ProofContracts.Module, typeof(ProofProduced), EventVisibility.Published),
            new EventDescriptor(ProofContracts.Assessed, ProofContracts.Module, typeof(ProofAssessed), EventVisibility.Published),
        ],
        [ProofContracts.Produced, ProofContracts.Assessed],
        [new ReshapeDescriptor(ProofContracts.Produced, ProofContracts.Assessed, ProofContracts.Module)],
        [],
        [ProofContracts.Classifier]);
}
