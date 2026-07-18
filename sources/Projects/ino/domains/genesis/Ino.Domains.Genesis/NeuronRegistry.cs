using System.Collections.Concurrent;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Genesis.Contracts;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;

namespace Ino.Domains.Genesis;

/// <summary>
/// In-memory <see cref="INeuronRegistry"/> backing the L1 loop.
/// Single activation cluster-wide — Orleans defaults converge it on the
/// Genesis silo because that's the only silo that loads this assembly.
/// State is volatile by design for v0.1; durable persistence is tracked
/// under issue #22 (ino-poc-phase-4 Slice E "out of scope" list).
///
/// The registry is a simple key→string map. Two writers exist today:
/// <c>CreatorNeuron</c> on a successfully compiled <c>L1Proposal</c>, and
/// the test acceptance fixture which seeds bodies directly. Reads come
/// from <c>RoslynPlan.ExecuteAsync</c> at every dynamic-neuron routing
/// hop.
///
/// Phase 4 epilogue Slice 3A adds an <see cref="ApprovalRequired"/> flag
/// (bound from <c>Ino:Inspector:ApprovalRequired</c>, default true) and a
/// draft-stash flow. When <c>ApprovalRequired</c> is true, CreatorNeuron
/// calls <see cref="StashDraftAsync"/> instead of
/// <see cref="RegisterAsync"/>; the user approves via the inspector
/// which calls <see cref="ApproveAsync"/>, triggering registration +
/// NeuronCreated + ProposalDecided(Approved) broadcasts.
/// </summary>
public sealed class NeuronRegistry(
    IGrainFactory grainFactory,
    IFirePort firePort,
    IConfiguration? config = null,
    ILogger<NeuronRegistry>? log = null)
    : Grain, INeuronRegistry
{
    private readonly ConcurrentDictionary<string, string> _bodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DraftNeuron> _drafts = new(StringComparer.Ordinal);
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    private bool ApprovalRequired =>
        config?.GetValue("Ino:Inspector:ApprovalRequired", true) ?? true;

    public Task<bool> GetApprovalRequiredAsync(CancellationToken ct = default) =>
        Task.FromResult(ApprovalRequired);

    public Task RegisterAsync(string neuronId, string scriptBody, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neuronId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptBody);

        _bodies[neuronId] = scriptBody;
        _log.LogInformation(
            "NeuronRegistry: registered script body for {NeuronId} ({Bytes} bytes, total neurons: {Count})",
            neuronId, scriptBody.Length, _bodies.Count);
        return Task.CompletedTask;
    }

    public Task<string?> GetScriptBodyAsync(string neuronId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neuronId);
        return Task.FromResult(_bodies.TryGetValue(neuronId, out var body) ? body : null);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_bodies.Keys.ToArray());

    public async Task StashDraftAsync(string proposalId, DraftNeuron draft, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentNullException.ThrowIfNull(draft);
        // Idempotent — first writer wins
        if (!_drafts.TryAdd(proposalId, draft)) return;

        _log.LogInformation(
            "NeuronRegistry: stashed draft for {ProposalId} (neuronId={NeuronId})",
            proposalId, draft.NeuronId);

        // Record the pending entry in ProposalLog (kernel-pinned) via a cross-silo
        // grain call. This avoids ProposalLog implementing IReactsTo<L1Proposal>
        // alongside CreatorNeuron, which would cause broadcast ambiguity.
        try
        {
            var proposalLog = grainFactory.GetGrain<IProposalLog>("singleton");
            var entry = new ProposalEntry(
                ProposalId: proposalId,
                UserId: draft.UserId,
                ClusterKey: draft.ClusterKey,
                ExamplePrompt: draft.ExamplePrompt,
                AllPrompts: draft.Definition.PromptExamples,
                Occurrences: draft.Occurrences,
                ProposedAt: draft.DraftedAt,
                Status: ProposalStatus.Pending,
                ActivatedNeuronId: null,
                DecidedAt: null,
                DecidedBy: null);
            await proposalLog.RecordPendingAsync(entry);
        }
        catch (Exception ex)
        {
            // ProposalLog is best-effort — never fail the stash on a log write error.
            _log.LogWarning(ex,
                "NeuronRegistry: ProposalLog.RecordPendingAsync failed for {ProposalId}", proposalId);
        }
    }

    public async Task<bool> ApproveAsync(string proposalId, string approvedBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedBy);

        if (!_drafts.Remove(proposalId, out var draft)) return false;

        // 1. Register script body
        await RegisterAsync(draft.NeuronId, draft.ScriptBody, ct);

        // 2. Register with Discovery so Cortex picks it up on next routing pass
        var discovery = grainFactory.GetGrain<IDiscovery>(0);
        await discovery.RegisterDynamicNeuronAsync(draft.Definition, ct);

        // Build a minimal NeuronContext for broadcasting. NeuronCreated +
        // ProposalDecided are fire-and-forget; failures are logged, not thrown.
        var ctx = BuildRegistryContext(proposalId);

        // 3. Broadcast NeuronCreated (closes the L1 loop)
        try
        {
            await firePort.FireBroadcast(
                new NeuronCreated(proposalId, draft.NeuronId, draft.UserId, DateTimeOffset.UtcNow),
                ctx, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "NeuronRegistry: NeuronCreated broadcast failed for proposal {ProposalId}", proposalId);
        }

        // 4. Broadcast ProposalDecided(Approved) so ProposalLog flips the status
        try
        {
            await firePort.FireBroadcast(
                new ProposalDecided(proposalId, ProposalStatus.Approved, approvedBy, DateTimeOffset.UtcNow),
                ctx, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "NeuronRegistry: ProposalDecided(Approved) broadcast failed for proposal {ProposalId}", proposalId);
        }

        _log.LogInformation(
            "NeuronRegistry: approved proposal {ProposalId} as {NeuronId} by {ApprovedBy}",
            proposalId, draft.NeuronId, approvedBy);
        return true;
    }

    public async Task<bool> RejectAsync(string proposalId, string rejectedBy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejectedBy);

        if (!_drafts.Remove(proposalId, out var draft)) return false;

        // Broadcast ProposalDecided(Rejected) so ProposalLog flips the status
        var ctx = BuildRegistryContext(proposalId);
        try
        {
            await firePort.FireBroadcast(
                new ProposalDecided(proposalId, ProposalStatus.Rejected, rejectedBy, DateTimeOffset.UtcNow),
                ctx, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "NeuronRegistry: ProposalDecided(Rejected) broadcast failed for proposal {ProposalId}", proposalId);
        }

        _log.LogInformation(
            "NeuronRegistry: rejected proposal {ProposalId} by {RejectedBy}",
            proposalId, rejectedBy);
        return true;
    }

    /// <summary>
    /// Builds a minimal <see cref="NeuronContext"/> for broadcasts emitted
    /// by the registry. The registry doesn't have a user identity — broadcasts
    /// use an empty UserId; downstream listeners should use the payload's own
    /// UserId field when user identity matters.
    /// </summary>
    NeuronContext BuildRegistryContext(string correlationSuffix) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId($"registry-{correlationSuffix}"),
            Source: new Caller.FromDomain(DomainId.From("genesis")),
            SourceStream: new StreamKey("neuron-registry"),
            UserId: string.Empty)
        {
            FirePort = firePort,
            Logger = _log,
        };
}
