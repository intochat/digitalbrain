using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Travel;
using Ino.Domains.Travel.Contracts;
using Xunit;

namespace Ino.Core.Tests;

public class TravelDeclaredNeuronsTests
{
    [Fact]
    public void Travel_declares_five_user_verb_neurons()
    {
        IDomain travel = new Travel();

        // Five user verbs — plan-trip, find-flights, find-hotels, find-places,
        // monitor-flight. The FlightDelayed synapse is a reactive broadcast
        // fired by FlightMonitorNeuron, not a user verb, so it is NOT declared
        // as a neuron.
        Assert.Equal(5, travel.DeclaredNeurons.Count);
    }

    [Fact]
    public void Travel_does_not_declare_FlightDelayed_as_a_user_verb()
    {
        IDomain travel = new Travel();

        Assert.DoesNotContain(travel.DeclaredNeurons, e => e.CanonicalSynapseType == typeof(FlightDelayed));
    }

    [Fact]
    public void Travel_plan_trip_neuron_points_at_PlanTripRequest()
    {
        IDomain travel = new Travel();

        var planTrip = travel.DeclaredNeurons
            .Single(e => e.Id == NeuronId.From("travel.plan-trip"));

        Assert.Equal("Plan a trip", planTrip.DisplayName);
        Assert.Equal(typeof(PlanTripRequest), planTrip.CanonicalSynapseType);
        Assert.NotEmpty(planTrip.PromptExamples);
    }

    [Fact]
    public void Travel_find_flights_neuron_points_at_FindFlightsRequest()
    {
        IDomain travel = new Travel();

        var findFlights = travel.DeclaredNeurons
            .Single(e => e.Id == NeuronId.From("travel.find-flights"));

        Assert.Equal(typeof(FindFlightsRequest), findFlights.CanonicalSynapseType);
    }
}
