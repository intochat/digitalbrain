using Ino.Core;

namespace Ino.Domains.Genesis.Contracts;

/// <summary>
/// Sentinel synapse shared by every dynamic (runtime-registered) neuron.
/// Each <see cref="Ino.Core.Hosting.INeuronDefinition"/> registered via
/// <c>CreatorNeuron</c> declares <see cref="Ino.Core.Hosting.INeuronDefinition.CanonicalSynapseType"/>
/// as <see cref="DynamicNeuronTrigger"/> so Discovery's canonical-handler
/// gate in <c>CortexNeuron.TryRouteToAsync</c> resolves to the single
/// pre-registered <c>RoslynPlan</c> grain — no manifest mutation is needed
/// at runtime, only the <see cref="IRoslynPlan"/> script body is dynamic.
///
/// The differentiator between dynamic neurons is
/// <see cref="Ino.Core.Hosting.NeuronPlanContext.NeuronId"/>, which the
/// plan looks up against <see cref="INeuronRegistry"/> to fetch the
/// compiled script body for that specific neuron.
/// </summary>
[GenerateSerializer]
public sealed record DynamicNeuronTrigger(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string NeuronId,
    [property: Id(2)] string UserId) : ISynapse;
