using Ino.Core.Hosting;
using Ino.Kernel.Contracts;
using Orleans;

namespace Ino.Kernel;

/// <summary>
/// Per-user tracker for <see cref="UnroutedIntent"/>s. Counts near-duplicate
/// prompts and emits an <see cref="Contracts.L1Proposal"/> broadcast when a
/// cluster crosses the threshold — Phase 4 Slice E.1 plumbing for the L1
/// self-improvement loop (issue #25).
///
/// Keyed by user id. Cortex's <c>EmitUnroutedAsync</c> calls
/// <see cref="RecordAsync"/> directly with the user id; the tracker
/// inherits <see cref="IJournaledNeuronQuery{TEvent}"/> via
/// <see cref="Neuron{TEvent}"/> so future cross-silo readers (a Genesis
/// silo's CreatorNeuron) can inspect the missed-intent journal.
/// </summary>
public interface IMissedIntentTracker : IGrainWithStringKey, IJournaledNeuronQuery<UnroutedIntent>
{
    /// <summary>
    /// Append an unrouted prompt and emit <see cref="Contracts.L1Proposal"/>
    /// if this prompt's normalised cluster just crossed the threshold.
    /// Idempotent for the proposal — only one broadcast per cluster, even
    /// if more matching prompts arrive later.
    /// </summary>
    Task RecordAsync(string text, string correlationId);
}
