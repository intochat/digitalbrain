using Ino.Core.Hosting;
using Ino.Kernel.Contracts;

namespace Ino.Domains.Travel.Contracts;

/// <summary>
/// Cross-silo plan grain interface for the <c>travel.plan-trip</c>
/// neuron. Multi-step trip planning with RFW callback support — extends
/// <see cref="IRfwEventHandler"/> so the grain reference proxy generated for
/// this interface implements both surfaces, letting the gateway pattern-match
/// a typed grain ref to <see cref="IRfwEventHandler"/> for event dispatch
/// without cross-silo grain reference activator setup.
/// </summary>
public interface ITripPlanner : INeuronPlan, IRfwEventHandler
{
}
