using Ino.Core;

namespace Ino.Core.Hosting;

public sealed class DiscoveryConflictException : Exception
{
    public DiscoveryConflictException(string message) : base(message) { }

    public static DiscoveryConflictException Canonical(
        Type synapseType,
        Type existingGrainType, DomainId existingSilo,
        Type newGrainType, DomainId newSilo)
    {
        return new DiscoveryConflictException(
            $"{newGrainType.FullName} in silo {newSilo} cannot register as canonical handler for " +
            $"{synapseType.FullName} — already registered to {existingGrainType.FullName} in silo {existingSilo}.");
    }
}
