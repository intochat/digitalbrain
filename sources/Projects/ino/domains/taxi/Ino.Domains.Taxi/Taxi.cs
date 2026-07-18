using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Taxi.Contracts;
using Ino.Domains.Taxi.Plans;

namespace Ino.Domains.Taxi;

/// <summary>
/// The Taxi domain. Declares two user-verb neurons:
/// <list type="bullet">
///   <item><description><c>taxi.find-ride</c> — single-hop (legacy) — Cortex
///   fires <see cref="FindRideRequest"/> with the prompt as pickup.</description></item>
///   <item><description><c>taxi.ride-home</c> — multi-hop plan — Cortex
///   dispatches to <see cref="IOrderRideHomePlan"/>, which BFS-walks the
///   user's Location journal to resolve home + current pickup, then fires
///   <see cref="FindRideRequest"/> with both endpoints filled in.</description></item>
/// </list>
/// </summary>
public sealed class Taxi : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Taxi");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Llm(LlmTier.Balanced),
    ];

    public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
    [
        new NeuronDefinition(
            NeuronId.From("taxi.find-ride"),
            DisplayName: "Find a ride",
            Description: "Hail a ride to a destination.",
            CanonicalSynapseType: typeof(FindRideRequest),
            PromptExamples: [
                "get me a ride",
                "book a taxi to the airport",
                "call an uber"
            ])
        {
            PlanType = typeof(IFindRidePlan),
        },
        new NeuronDefinition(
            NeuronId.From("taxi.ride-home"),
            DisplayName: "Ride home",
            Description:
                "Hail a ride to the user's home, inferring pickup from recent location and home from a saved anchor.",
            CanonicalSynapseType: typeof(FindRideRequest),
            PromptExamples: [
                "take me home",
                "ride home",
                "uber home",
                "taxi back home"
            ])
        {
            PlanType = typeof(IOrderRideHomePlan),
        },
    ];
}
