using Ino.Core.Hosting;

namespace Ino.Domains.Taxi.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>taxi.find-ride</c> neuron.
/// One-hop forward of the user prompt to <see cref="FindRideRequest"/>, with
/// the prompt passed as Pickup and Dropoff left empty — the legacy switch
/// behaviour preserved verbatim. The richer <c>taxi.ride-home</c> neuron
/// uses <see cref="IOrderRideHomePlan"/> for multi-hop resolution.
/// </summary>
public interface IFindRidePlan : INeuronPlan
{
}
