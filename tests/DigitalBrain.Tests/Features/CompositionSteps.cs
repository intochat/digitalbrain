using System.Text.Json;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Mcp;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class CompositionSteps(BrainWorld world, BrainSteps brain)
{
    [Given("a running brain with UI")]
    public async Task GivenUi()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(UIModule)]),
        });

    [Then(@"chart ""(.*)"" has (\d+) points")]
    public async Task ChartPointCount(string name, int count)
    {
        var chart = world.Brain.Grains.GetGrain<IChart>(new DigitalBrain.Abstractions.Identity.NeuronId("chart", name).ToGrainId());
        Assert.Equal(count, (await chart.Read()).Points.Count);
    }

    [When(@"""(.*)"" calls ""(.*)"" ""(.*)"" on ""(.*)"" with (\{.*\})$")]
    public async Task Call(string session, string interfaceAlias, string methodAlias, string neuron, string json)
    {
        using var document = JsonDocument.Parse(json);
        var operations = new BrainOperations(world.Brain.Grains, world.Brain.SiloServices.GetRequiredService<INeuronInvoker>());
        try
        {
            await operations.CallAsync(session, new CallRequest(neuron, interfaceAlias, methodAlias, document.RootElement));
        }
        catch (Exception error)
        {
            brain.RecordError(error);
            throw;
        }
    }
}
