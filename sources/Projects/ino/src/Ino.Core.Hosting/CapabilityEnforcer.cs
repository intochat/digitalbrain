using Ino.Core;

namespace Ino.Core.Hosting;

public sealed class CapabilityEnforcer(IReadOnlyDictionary<DomainId, IReadOnlyList<Capability>> declarationsBySource)
    : ICapabilityEnforcer
{
    public void AssertCanFire(Caller source, CanonicalTarget target)
    {
        // KERNEL BYPASS: Caller.Ambient skips enforcement unconditionally. The
        // ambient identity represents a fire originating from kernel DI (via
        // IAmbientFire — see AmbientFire.BuildContext) rather than an installed
        // neuron bundle. Kernel code is trusted; neuron code is not.
        //
        // This means: any DI-resolvable IAmbientFire is effectively a sandbox
        // escape for whichever component owns it. IAmbientFire MUST NOT be
        // registered in NeuronDefinition-silo DI where bundles can resolve it, and
        // it MUST NOT be exposed through NeuronContext. Keep it scoped to
        // kernel-initiated fires (System/Identity silos, hosted services).
        if (source is Caller.Ambient) return;
        if (source is not Caller.FromDomain domain)
            throw new InvalidOperationException($"Unexpected Caller subtype: {source.GetType()}");

        if (!declarationsBySource.TryGetValue(domain.Domain, out var declared))
            throw new CapabilityDeniedException(
                $"Domain {domain.Domain} is not registered — cannot fire {target.SynapseType.FullName}.",
                new Dictionary<string, string> { ["domain"] = domain.Domain.Value });

        var missing = target.RequiredCapabilities
            .Where(req => !declared.Any(d => d.Equals(req)))
            .ToArray();

        if (missing.Length > 0)
            throw new CapabilityDeniedException(
                $"Domain {domain.Domain} does not declare required capabilities for " +
                $"{target.GrainType.FullName}: {string.Join(", ", missing)}",
                new Dictionary<string, string>
                {
                    ["domain"] = domain.Domain.Value,
                    ["target"] = target.GrainType.FullName ?? target.GrainType.Name,
                    ["missing"] = string.Join("|", missing),
                });
    }

    public void AssertCanFireBroadcast(Caller source, ReactiveTarget target)
    {
    }
}
