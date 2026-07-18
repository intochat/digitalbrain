using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel;

/// <summary>
/// The Travel domain. Declares 5 user-verb neurons (plan-trip,
/// find-flights, find-hotels, find-places, monitor-flight). Each routable
/// neuron sets <see cref="INeuronDefinition.PlanType"/> so Cortex dispatches
/// via <see cref="INeuronPlan"/> rather than knowing about
/// <see cref="FindFlightsRequest"/>/etc. by type — see Phase 4 Slice A.
/// The <c>FlightDelayed</c> synapse is a reactive broadcast fired by
/// <c>FlightMonitorNeuron</c> — not a user verb — and so is not declared
/// here. Integrates with TripRadar (external, at <c>tripradar/</c>) for
/// real data; neurons ship per slice.
/// </summary>
public sealed class Travel : IDomain
{
    public DomainId Id => DomainId.From("Ino.Domains.Travel");
    public string Version => "0.1.0";

    public IReadOnlyList<Capability> DeclaredCapabilities =>
    [
        new Capability.Llm(LlmTier.Balanced),
    ];

    public IReadOnlyList<INeuronDefinition> DeclaredNeurons =>
    [
        new NeuronDefinition(
            NeuronId.From("travel.plan-trip"),
            DisplayName: "Plan a trip",
            Description: "Build an itinerary with flights, hotels, and things to do.",
            CanonicalSynapseType: typeof(PlanTripRequest),
            PromptExamples: [
                "plan a trip to bali",
                "help me plan 5 days in tokyo",
                "i want to visit lisbon next month"
            ])
        {
            PlanType = typeof(ITripPlanner),
        },
        new NeuronDefinition(
            NeuronId.From("travel.find-flights"),
            DisplayName: "Find flights",
            Description: "Search flights between two cities on a given date.",
            CanonicalSynapseType: typeof(FindFlightsRequest),
            PromptExamples: [
                "find flights to bali",
                "cheapest flight from berlin to tokyo"
            ])
        {
            PlanType = typeof(IFindFlightsPlan),
        },
        new NeuronDefinition(
            NeuronId.From("travel.find-hotels"),
            DisplayName: "Find hotels",
            Description: "Search hotels at a destination by rating, price, and amenities.",
            CanonicalSynapseType: typeof(FindHotelsRequest),
            PromptExamples: [
                "find a hotel in bali",
                "hotels near shibuya for 3 nights"
            ])
        {
            PlanType = typeof(IFindHotelsPlan),
        },
        new NeuronDefinition(
            NeuronId.From("travel.find-places"),
            DisplayName: "Find things to do",
            Description: "Suggest activities and places to visit at your destination.",
            CanonicalSynapseType: typeof(FindPlacesRequest),
            PromptExamples: [
                "things to do in bali",
                "what's good to see in lisbon"
            ])
        {
            PlanType = typeof(IFindPlacesPlan),
        },
        new NeuronDefinition(
            NeuronId.From("travel.monitor-flight"),
            DisplayName: "Monitor a flight",
            Description: "Watch for delays or gate changes and notify when they happen.",
            CanonicalSynapseType: typeof(ArmFlightMonitor),
            PromptExamples: [
                "watch my flight",
                "let me know if BA123 is delayed",
                "notify me when my flight is delayed"
            ]),
        // travel.monitor-flight stays plan-less for now — its synapse takes
        // multi-slot args (flight number, date) that need extraction beyond
        // a prompt-passthrough plan. Tracked as a follow-up to Slice A.
    ];
}
