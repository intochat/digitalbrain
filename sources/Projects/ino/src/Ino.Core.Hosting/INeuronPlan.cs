using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Multi-hop neuron executor — the BFS that visits neurons across silos,
/// reads their journals, picks next hops, and fires synapses to satisfy the
/// user's intent. Plans are grains so each domain ships its plans alongside
/// its neurons in the same NuGet/silo, and so a plan can journal its own
/// reasoning if it inherits from <see cref="Neuron{TEvent}"/>.
///
/// Cortex resolves a plan by <see cref="INeuronDefinition.PlanType"/> (the closed
/// grain interface, e.g. <c>IOrderRideHomePlan</c>), grabs a grain via
/// <see cref="IGrainFactory"/>, and calls <see cref="ExecuteAsync"/>. The
/// plan implementation builds an <see cref="ITraversalEngine"/> in-domain
/// from its own DI services and walks the graph from there.
///
/// A plan's grain primary key is the user id (or correlation id when no user
/// is bound). Per-user keying lets stateful plans accumulate decisions over
/// time without an explicit memory store.
/// </summary>
public interface INeuronPlan : IGrainWithStringKey
{
    Task<NeuronResult> ExecuteAsync(NeuronPlanContext input, CancellationToken ct = default);
}
