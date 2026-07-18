using Ino.Core.Hosting;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>travel.find-flights</c>
/// neuron. Cortex resolves this when the neuron's <see cref="INeuronDefinition.PlanType"/>
/// is set, replacing the legacy hardcoded switch in <c>CortexNeuron</c>.
/// The plan body is one-hop — pass the prompt through to <see cref="FindFlightsRequest"/> —
/// but going through a plan keeps the kernel free of per-domain synapse types.
/// </summary>
public interface IFindFlightsPlan : INeuronPlan
{
}
