using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Genesis.Compilation;
using Ino.Domains.Genesis.Contracts;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace Ino.Domains.Genesis.Neurons;

/// <summary>
/// L1 self-improvement consumer. Reacts to <see cref="L1Proposal"/>
/// broadcasts emitted by <c>Ino.Kernel.MissedIntentTracker</c>, drafts a
/// trivial echo script body for the cluster prompt, validates it via
/// <see cref="PlanCompiler.Validate"/>, and — depending on
/// <see cref="INeuronRegistry.GetApprovalRequiredAsync"/> — either
/// registers the resulting dynamic neuron immediately or stashes it
/// as a draft pending user approval via the inspector.
///
/// v0.1 draft body is a deterministic stub — <c>NeuronResult.Ok($"Got it,
/// I'll help with: {Prompt}.")</c> — so the loop closes without depending
/// on an LLM. Synthesising richer bodies (LLM-driven from the proposal's
/// example prompts and rationale) is post-acceptance scope; the seam
/// here is intentionally narrow.
///
/// Idempotent per <see cref="L1Proposal.ProposalId"/>: re-firing the same
/// proposal is a no-op so duplicate broadcasts (Aspire restart, journal
/// replay) don't churn the registry.
/// </summary>
public sealed class CreatorNeuron(
    IGrainFactory grainFactory,
    IFirePort firePort,
    ILogger<CreatorNeuron>? log = null)
    : Grain, IReactsTo<L1Proposal>
{
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;
    private readonly HashSet<string> _seenProposals = new(StringComparer.Ordinal);

    public async Task ReactAsync(L1Proposal synapse, NeuronContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        if (!_seenProposals.Add(synapse.ProposalId))
        {
            _log.LogDebug(
                "CreatorNeuron: ignoring duplicate L1Proposal {ProposalId}",
                synapse.ProposalId);
            return;
        }

        var neuronId = DraftNeuronId(synapse);
        var scriptBody = DraftScriptBody(synapse);

        if (PlanCompiler.Validate(scriptBody) is { } compileError)
        {
            _log.LogError(
                "CreatorNeuron: draft script for {NeuronId} failed to compile — {Error}",
                neuronId, compileError);
            await firePort.FireBroadcast(
                new NeuronActivationFailed(
                    synapse.ProposalId, neuronId, synapse.UserId, compileError, DateTimeOffset.UtcNow),
                ctx,
                ct);
            return;
        }

        var registry = grainFactory.GetGrain<INeuronRegistry>(0);
        var approvalRequired = await registry.GetApprovalRequiredAsync(ct);

        if (approvalRequired)
        {
            // Stash the draft; user will approve via the inspector, which
            // calls NeuronRegistry.ApproveAsync. The broadcasts
            // (NeuronCreated + ProposalDecided) fire from there so the
            // ProposalLog lifecycle is complete without an extra round-trip.
            var draft = ComposeDraft(synapse, neuronId, scriptBody);
            await registry.StashDraftAsync(synapse.ProposalId, draft, ct);
            _log.LogInformation(
                "CreatorNeuron: stashed draft for {ProposalId} (neuronId={NeuronId}, ApprovalRequired=true)",
                synapse.ProposalId, neuronId);
            return;
        }

        // ApprovalRequired=false: register immediately (same as pre-Slice-3A behaviour).
        await registry.RegisterAsync(neuronId, scriptBody, ct);

        var neuron = BuildNeuronDefinition(synapse, neuronId);
        var discovery = grainFactory.GetGrain<IDiscovery>(0);
        await discovery.RegisterDynamicNeuronAsync(neuron, ct);

        await firePort.FireBroadcast(
            new NeuronCreated(synapse.ProposalId, neuronId, synapse.UserId, DateTimeOffset.UtcNow),
            ctx,
            ct);

        _log.LogInformation(
            "CreatorNeuron: registered dynamic neuron {NeuronId} for proposal {ProposalId} (user {UserId})",
            neuronId, synapse.ProposalId, synapse.UserId);
    }

    /// <summary>
    /// Derives a stable neuron id from the proposal's cluster key.
    /// Stable so that re-firing the same proposal hits the same registry
    /// slot (the duplicate-proposal check is the first line of defence;
    /// stable ids are belt-and-braces). Format: <c>genesis.&lt;slug&gt;</c>
    /// where slug is the cluster key collapsed to ASCII identifier chars
    /// truncated to 32 chars.
    /// </summary>
    public static string DraftNeuronId(L1Proposal synapse)
    {
        var slug = new string(synapse.ClusterKey
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());
        slug = slug.Trim('-');
        if (slug.Length > 32) slug = slug[..32];
        if (slug.Length == 0) slug = synapse.ProposalId.ToLowerInvariant();
        return $"genesis.{slug}";
    }

    /// <summary>
    /// v0.1 deterministic draft: an echo script that confirms the prompt
    /// landed. Sophisticated synthesis (LLM-driven from proposal context,
    /// fanning out to existing canonical handlers, etc.) is post-acceptance.
    /// The shape MUST evaluate to a <c>Task&lt;NeuronResult&gt;</c>-returning
    /// expression so <see cref="PlanCompiler.ExecuteAsync"/> succeeds.
    /// </summary>
    public static string DraftScriptBody(L1Proposal synapse) =>
        $"return NeuronResult.Ok($\"Got it — I'll help with '{{Prompt}}'. (Auto-generated from {synapse.Occurrences} unrouted prompts.)\");";

    /// <summary>
    /// Builds the <see cref="DraftNeuron"/> snapshot that the registry
    /// stashes when <see cref="INeuronRegistry.GetApprovalRequiredAsync"/>
    /// is true.
    /// </summary>
    static DraftNeuron ComposeDraft(L1Proposal synapse, string neuronId, string scriptBody) =>
        new(
            NeuronId: neuronId,
            ScriptBody: scriptBody,
            ProposalId: synapse.ProposalId,
            DraftedAt: DateTimeOffset.UtcNow,
            Definition: BuildNeuronDefinition(synapse, neuronId),
            UserId: synapse.UserId,
            ClusterKey: synapse.ClusterKey,
            ExamplePrompt: synapse.ExamplePrompt,
            Occurrences: synapse.Occurrences);

    /// <summary>
    /// Builds the <see cref="Neuron"/> descriptor that Discovery exposes
    /// to Cortex after registration.
    /// </summary>
    static NeuronDefinition BuildNeuronDefinition(L1Proposal synapse, string neuronId) =>
        new(
            NeuronId.From(neuronId),
            DisplayName: $"Auto-generated handler for '{synapse.ClusterKey}'",
            Description: $"Created from L1 proposal {synapse.ProposalId} after {synapse.Occurrences} unrouted prompts.",
            CanonicalSynapseType: typeof(DynamicNeuronTrigger),
            PromptExamples: [synapse.ExamplePrompt])
        {
            PlanType = typeof(IRoslynPlan),
        };
}
