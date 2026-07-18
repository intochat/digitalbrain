using System.Text.RegularExpressions;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Journaling;

namespace Ino.Kernel;

/// <summary>
/// Default <see cref="IMissedIntentTracker"/> implementation. User-keyed
/// <see cref="Neuron{TEvent}"/> over <see cref="UnroutedIntent"/> — every
/// call to <see cref="RecordAsync"/> appends to the journal, then derives
/// the cluster counts on the fly. Tiny user histories make on-demand
/// counting cheaper than a parallel <see cref="IDurableDictionary{TKey, TValue}"/>;
/// when journals get large the count can be cached.
///
/// L1Proposal is fired exactly once per cluster — the journal records both
/// the unrouted prompts and an in-band sentinel (<see cref="UnroutedIntent.UserId"/>
/// set to <see cref="L1ProposalEmittedSentinel"/>) when a proposal goes
/// out, so subsequent records skip re-firing for the same cluster.
/// </summary>
public sealed class MissedIntentTracker(
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<UnroutedIntent>> journal,
    IFirePort? firePort = null,
    ILogger<MissedIntentTracker>? log = null)
    : Neuron<UnroutedIntent>(journal), IMissedIntentTracker
{
    /// <summary>
    /// In-band marker stored in <see cref="UnroutedIntent.UserId"/> when the
    /// cluster has emitted its proposal. Avoids needing a sibling
    /// <see cref="IDurableDictionary{TKey, TValue}"/> just to track which
    /// clusters are "done."
    /// </summary>
    public const string L1ProposalEmittedSentinel = "<l1-proposal-emitted>";

    /// <summary>How many matching prompts trigger an L1Proposal.</summary>
    public const int ClusterThreshold = 3;

    private readonly IFirePort _firePort = firePort ?? new NoOpFirePort();
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    public async Task RecordAsync(string text, string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var userId = this.GetPrimaryKeyString();
        var ctx = BuildSelfContext(correlationId);

        var clusterKey = NormalizeForCluster(text);
        var alreadyEmitted = false;
        var matching = 0;
        // Read history through the Neuron base API rather than the captured
        // IDurableList ctor parameter — primary-constructor captures of args
        // already passed to the base trigger CS9107.
        var history = await GetHistoryAsync(int.MaxValue);
        foreach (var payload in history)
        {
            if (NormalizeForCluster(payload.Text) != clusterKey) continue;
            if (string.Equals(payload.UserId, L1ProposalEmittedSentinel, StringComparison.Ordinal))
                alreadyEmitted = true;
            else
                matching++;
        }

        await RaiseAsync(new UnroutedIntent(text, userId), ctx);
        matching++;

        if (alreadyEmitted || matching < ClusterThreshold) return;

        // Mark the cluster as proposed (journal sentinel) and broadcast.
        // Fire-and-forget broadcast — listeners (future Genesis CreatorNeuron)
        // pick it up off the wire; the kernel doesn't wait.
        await RaiseAsync(new UnroutedIntent(text, L1ProposalEmittedSentinel), ctx);

        var proposal = new L1Proposal(
            ProposalId: Ulid.NewUlid().ToString(),
            UserId: userId,
            ClusterKey: clusterKey,
            ExamplePrompt: text,
            Occurrences: matching,
            ProposedAt: DateTimeOffset.UtcNow);

        try
        {
            await _firePort.FireBroadcast(proposal, ctx);
            _log.LogInformation(
                "MissedIntentTracker: emitted L1Proposal {Proposal} for user {User} cluster {Cluster} after {Count} occurrences",
                proposal.ProposalId, userId, clusterKey, matching);
        }
        catch (Exception ex)
        {
            // If broadcast fails the cluster stays marked; we don't
            // re-attempt. The journal still records the surge for an
            // operator/inspector to act on manually.
            _log.LogWarning(ex,
                "MissedIntentTracker: L1Proposal broadcast failed for user {User} cluster {Cluster}",
                userId, clusterKey);
        }
    }

    NeuronContext BuildSelfContext(string correlationId) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId(correlationId),
            Source: new Caller.FromDomain(DomainId.From("kernel")),
            SourceStream: new StreamKey($"missed-intent:{this.GetPrimaryKeyString()}"),
            UserId: this.GetPrimaryKeyString())
        {
            FirePort = _firePort,
            Logger = _log,
        };

    static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Normalise a prompt for cluster comparison: lowercase, trim, collapse
    /// whitespace, strip trailing punctuation. v0.1 only — embeddings land
    /// post-v0.1 with the IAW substrate's IEmbeddingGenerator.
    /// </summary>
    public static string NormalizeForCluster(string text) =>
        Whitespace.Replace(text.ToLowerInvariant().Trim(), " ").TrimEnd('.', '!', '?', ',', ';', ':');
}
