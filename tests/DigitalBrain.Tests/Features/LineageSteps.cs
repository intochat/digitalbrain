using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class LineageSteps(BrainSteps brain, CommandSteps commands)
{
    [Given(@"""(.*)"" is connected to counter ""(.*)"" for ""(.*)""")]
    public Task ConnectToCounter(string listener, string name, string type)
    {
        // The synapse runs counter -> listener so the listener receives the counter's signal.
        return brain.Brain.Grains.GetGrain<INeuron>(new NeuronId("counter", name).ToGrainId())
            .Connect(BrainSteps.Id(listener), type);
    }

    [Then(@"the latest ""(.*)"" incoming entry causation equals the work id of ""(.*)""")]
    public async Task ThenCausationEqualsWork(string name, string handle)
    {
        var read = await brain.Journal(name, JournalKind.Incoming);
        Assert.NotEmpty(read.Delta);
        Assert.Equal(commands.WorkByCommand[handle], read.Delta[^1].CausationId);
    }

    [Then(@"""(.*)"" commands journal record ""(.*)"" causation is empty")]
    public async Task ThenCommandCausationIsEmpty(string name, string handle)
    {
        var read = await brain.Brain.Grains.GetGrain<INeuron>(new NeuronId("counter", name).ToGrainId()).ReadCommands(0);
        var records = read.Delta.Where(record => record.Id == CommandSteps.CommandIdFrom(handle)).ToArray();
        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.Null(record.Causation));
    }
}
