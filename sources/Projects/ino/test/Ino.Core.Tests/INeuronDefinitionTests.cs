using Ino.Core;
using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Tests;

public class INeuronDefinitionTests
{
    [Fact]
    public void NeuronDefinition_record_round_trips_all_five_fields()
    {
        var id = NeuronId.From("travel.plan-trip");
        var examples = new[] { "plan a trip to bali", "help me plan 5 days in tokyo" };

        INeuronDefinition exp = new NeuronDefinition(
            Id: id,
            DisplayName: "Plan a trip",
            Description: "Build an itinerary with flights, hotels, and things to do.",
            CanonicalSynapseType: typeof(FakeSynapse),
            PromptExamples: examples);

        Assert.Equal(id, exp.Id);
        Assert.Equal("Plan a trip", exp.DisplayName);
        Assert.Equal("Build an itinerary with flights, hotels, and things to do.", exp.Description);
        Assert.Equal(typeof(FakeSynapse), exp.CanonicalSynapseType);
        Assert.Equal(examples, exp.PromptExamples);
    }

    [Fact]
    public void Two_NeuronDefinition_records_with_same_inputs_are_equal()
    {
        var examples = new[] { "one" };
        var a = new NeuronDefinition(
            NeuronId.From("x.y"), "Y", "desc", typeof(FakeSynapse), examples);
        var b = new NeuronDefinition(
            NeuronId.From("x.y"), "Y", "desc", typeof(FakeSynapse), examples);

        Assert.Equal(b, a);
    }

    private sealed record FakeSynapse : ISynapse;
}
