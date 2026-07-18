using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Cluster-wide registry for dynamic neuron script bodies. Phase 4
/// Slice E.2 (issue #25): the L1 self-improvement loop adds runtime
/// neurons without restarting any silo by storing each neuron's
/// Roslyn script body here, keyed by neuron id. The single shared
/// <c>RoslynPlan</c> grain in <c>Ino.Domains.Genesis</c> looks the body
/// up at <see cref="INeuronPlan.ExecuteAsync"/> time and runs it via
/// <c>Microsoft.CodeAnalysis.CSharp.Scripting</c>.
///
/// One activation per cluster (Orleans places it on whichever silo hosts
/// the grain class — only Genesis does, so it converges there). v0.1
/// holds bodies in memory; durable persistence falls out of issue #22 and
/// is documented as a known gap.
///
/// Phase 4 epilogue Slice 3A: adds an <see cref="ApprovalRequired"/> flag
/// (default <see langword="true"/> from config key
/// <c>Ino:Inspector:ApprovalRequired</c>) and a draft-stash flow so
/// <see cref="CreatorNeuron"/> can gate L1 registration behind user approval.
/// </summary>
public interface INeuronRegistry : IGrainWithIntegerKey
{
    /// <summary>
    /// When <see langword="true"/> (default), <c>CreatorNeuron</c> stashes a
    /// <see cref="DraftNeuron"/> instead of immediately registering.
    /// The user approves through the inspector, which calls
    /// <see cref="ApproveAsync"/>.
    /// </summary>
    Task<bool> GetApprovalRequiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers (or replaces) the script body for a neuron. Idempotent
    /// per <paramref name="neuronId"/>.
    /// </summary>
    Task RegisterAsync(string neuronId, string scriptBody, CancellationToken ct = default);

    /// <summary>
    /// Returns the registered script body, or <see langword="null"/> when
    /// no neuron with that id has been registered.
    /// </summary>
    Task<string?> GetScriptBodyAsync(string neuronId, CancellationToken ct = default);

    /// <summary>
    /// Returns every registered neuron id. Used by tests + the (future)
    /// inspector "Proposals" pane.
    /// </summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Stash a draft for later approval. Idempotent on
    /// <paramref name="proposalId"/>.
    /// </summary>
    Task StashDraftAsync(string proposalId, DraftNeuron draft, CancellationToken ct = default);

    /// <summary>
    /// Promote a stashed draft to a live registered neuron. Returns
    /// <see langword="true"/> if an approval actually happened (false when the
    /// proposal was unknown or already approved).
    /// </summary>
    Task<bool> ApproveAsync(string proposalId, string approvedBy, CancellationToken ct = default);

    /// <summary>
    /// Discard a stashed draft. Returns <see langword="true"/> if a stash
    /// existed and was removed.
    /// </summary>
    Task<bool> RejectAsync(string proposalId, string rejectedBy, CancellationToken ct = default);
}

/// <summary>
/// Immutable snapshot of a draft neuron awaiting user approval.
/// Created by <c>CreatorNeuron</c> when
/// <see cref="INeuronRegistry.GetApprovalRequiredAsync"/> returns <see
/// langword="true"/>. Carries enough data for <see cref="INeuronRegistry"/>
/// to complete the full registration on approval (script body + dynamic
/// neuron metadata + originating user id).
/// </summary>
[GenerateSerializer]
public sealed record DraftNeuron(
    [property: Id(0)] string NeuronId,
    [property: Id(1)] string ScriptBody,
    [property: Id(2)] string ProposalId,
    [property: Id(3)] DateTimeOffset DraftedAt,
    [property: Id(4)] NeuronDefinition Definition,
    [property: Id(5)] string UserId,
    [property: Id(6)] string ClusterKey,
    [property: Id(7)] string ExamplePrompt,
    [property: Id(8)] int Occurrences);
