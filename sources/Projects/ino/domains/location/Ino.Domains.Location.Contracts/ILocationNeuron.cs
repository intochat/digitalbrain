using Ino.Core.Hosting;
using Orleans;

namespace Ino.Domains.Location.Contracts;

/// <summary>
/// Cross-silo grain interface for the location journal. Keyed by user id —
/// each user has their own location event log. Plans read via
/// <see cref="IJournaledNeuronQuery{TEvent}"/> (inherited); writes go through
/// the explicit <see cref="RecordAsync"/> method so location recording bypasses
/// <see cref="IFirePort"/> (whose canonical-handler path is correlation-keyed,
/// not user-keyed, and would scatter visits across ephemeral grain activations).
///
/// A future slice may add an <c>INeuron&lt;ObservedAtPlace&gt;</c> when location
/// inference becomes a passive reactor — but for v0.1 every recording is
/// driven by an explicit plan/user action.
/// </summary>
public interface ILocationNeuron : IGrainWithStringKey, IJournaledNeuronQuery<LocationVisited>
{
    /// <summary>
    /// Append a <see cref="LocationVisited"/> to the user's journal under the
    /// supplied correlation id. Stream/causation context is rebuilt from the
    /// caller-supplied correlation; <see cref="ILogger"/> + <see cref="IFirePort"/>
    /// are pulled from the grain's own DI on activation.
    /// </summary>
    Task RecordAsync(string place, string? label, string correlationId);
}
