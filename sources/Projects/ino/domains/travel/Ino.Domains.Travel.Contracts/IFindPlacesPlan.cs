using Ino.Core.Hosting;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>travel.find-places</c>
/// neuron. One-hop forward of the user prompt to
/// <see cref="FindPlacesRequest"/>.
/// </summary>
public interface IFindPlacesPlan : INeuronPlan
{
}
