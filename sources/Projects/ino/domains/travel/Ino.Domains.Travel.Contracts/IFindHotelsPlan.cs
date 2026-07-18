using Ino.Core.Hosting;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>travel.find-hotels</c>
/// neuron. One-hop forward of the user prompt to
/// <see cref="FindHotelsRequest"/>. See <see cref="IFindFlightsPlan"/> for the
/// pattern rationale.
/// </summary>
public interface IFindHotelsPlan : INeuronPlan
{
}
