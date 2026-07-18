using Ino.Core.Hosting;

namespace Ino.Domains.Taxi.Contracts;

/// <summary>
/// Cross-silo grain interface for the <c>taxi.ride-home</c> neuron plan.
/// Cortex resolves this via <see cref="INeuronDefinition.PlanType"/> when a
/// "ride home" / "take me home" prompt matches, then calls
/// <see cref="INeuronPlan.ExecuteAsync"/>. The plan is pinned to the
/// Taxi silo so it's co-located with <c>RideSearchNeuron</c> (the synapse
/// it ultimately fires).
/// </summary>
public interface IOrderRideHomePlan : INeuronPlan
{
}
