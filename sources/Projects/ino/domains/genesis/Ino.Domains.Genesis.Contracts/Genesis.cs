using Ino.Core;
using Ino.Core.Hosting;

namespace Ino.Domains.Genesis.Contracts;

/// <summary>
/// Genesis is the L1 self-improvement domain: it watches for
/// <c>L1Proposal</c> broadcasts emitted by the kernel's
/// <c>MissedIntentTracker</c>, drafts a script body for the missing
/// intent, and registers a new dynamic neuron via
/// <see cref="INeuronRegistry"/> — all without restarting any silo.
///
/// Genesis declares no end-user neurons. The dynamic ones land on a
/// single pre-registered <c>RoslynPlan</c> shell grain whose script body
/// is fetched per execution from the registry. The marker class lives in
/// <c>.Contracts</c> so test fixtures can construct an
/// <see cref="IDomain"/> instance without pulling the impl assembly.
/// </summary>
public sealed class Genesis : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Genesis");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities => Array.Empty<Capability>();

    public IReadOnlyList<INeuronDefinition> DeclaredNeurons => Array.Empty<INeuronDefinition>();
}
