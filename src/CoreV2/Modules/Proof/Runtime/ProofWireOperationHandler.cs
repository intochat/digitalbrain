using System.Text.Json;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Brain.Core.Runtime;

namespace Brain.Modules.Proof;

public sealed class ProofWireOperationHandler : IBrainOperationHandler
{
    private const string InputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"target":{"type":"string","const":"assessment"}},"required":["target"]}
        """;
    private const string ResultSchema = """
        {"type":"object","additionalProperties":false,"properties":{"synapseId":{"type":"string"},"revision":{"type":"integer"},"route":{"type":"string"}},"required":["synapseId","revision","route"]}
        """;

    public BrainOperationDescriptor Descriptor { get; } = new(
        "Proof.Wire@1",
        "proof",
        "Wire ProofProduced to assessment",
        InputSchema,
        ResultSchema);

    public async Task<string> ExecuteAsync(
        BrainOperationExecutionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = JsonDocument.Parse(context.Invocation.InputJson);
        if (!input.RootElement.TryGetProperty("target", out var target)
            || !string.Equals(target.GetString(), "assessment", StringComparison.Ordinal))
        {
            throw new JsonException("Proof.Wire@1 requires target 'assessment'.");
        }

        var graph = context.Grains.GetGrain<IBrainGraphGrain>(context.Invocation.WorkspaceId);
        var synapse = await graph.InstallAsync(new BrainSynapseChange(
            context.Invocation.WorkspaceId,
            new BrainNeuronView("proof/source/workspace", "proof", "source", "workspace", 0),
            new BrainNeuronView("proof/assessment/workspace", "proof", "assessment", "workspace", 0),
            "ProofProduced@1",
            "ProofProduced@1",
            context.ActivityId));
        await context.JournalAsync(
            "synapse-installed",
            "core/brain-graph/workspace",
            BrainJournalDirection.System,
            "BrainGraph.SynapseInstalled@1",
            "installed",
            "Installed ProofProduced@1 Synapse to assessment",
            synapseId: synapse.Id,
            synapseRevision: synapse.Revision);

        return JsonSerializer.Serialize(new
        {
            synapseId = synapse.Id.ToString("n"),
            revision = synapse.Revision,
            route = "assessment",
        });
    }
}
