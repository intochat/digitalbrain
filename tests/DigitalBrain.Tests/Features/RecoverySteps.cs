using System.Diagnostics;
using System.Runtime.ExceptionServices;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class RecoverySteps(BrainWorld world, BrainSteps brain)
{
    [Given("a running brain with faulting storage")]
    public async Task GivenAFaultingBrain()
        => world.Simulation = await BrainSteps.StartSimulationAsync(
            Path.Combine(Path.GetTempPath(), "digitalbrain-tests", Guid.NewGuid().ToString("N")), world.JournalFaults);

    [Given("storage fails the next write")]
    public void FailNextWrite() => world.JournalFaults.FailNextWrite();

    [Given("storage cancels the next write")]
    public void CancelNextWrite() => world.JournalFaults.CancelNextWrite();

    [Given(@"storage fails the write after counter ""(.*)"" records Attempted")]
    public void FailAfterAttempted(string name)
    {
        // The command wrapper's first write is Attempted and its second is the terminal record.
        world.JournalFaults.FailWriteNumber("counter/" + name, 2);
    }

    [When(@"""(.*)"" connects ""(.*)"" to plain ""(.*)"" for ""(.*)"" and the call fails")]
    public async Task ConnectAndFail(string _, string from, string target, string type)
    {
        var error = await Record.ExceptionAsync(() => brain.Neuron(from).Connect(NeuronId.Plain(target), type));
        Assert.NotNull(error);
    }

    [When(@"""(.*)"" fires ""(.*)"" at plain ""(.*)"" and the call fails")]
    public async Task FireAndFail(string from, string type, string target)
    {
        await brain.FireCore(from, type, "{}", NeuronId.Plain(target));
        Assert.NotNull(brain.LastError);
    }

    [Then(@"reading synapses of ""(.*)"" throws NeuronRecovering or shows no ""(.*)"" synapse")]
    public async Task ReadSynapsesDuringRecovery(string name, string type)
    {
        IReadOnlyList<Synapse>? synapses = null;
        var error = await Record.ExceptionAsync(async () => { synapses = await brain.Query(name).ReadSynapses(); });
        if (error is NeuronRecoveringException)
        {
            return;
        }

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        Assert.NotNull(synapses);
        Assert.DoesNotContain(synapses, synapse => synapse.SignalType == type);
    }

    [Then(@"after storage recovers, ""(.*)"" synapses do not include ""(.*)"" to ""(.*)""")]
    public async Task ReadSynapsesAfterRecovery(string name, string type, string target)
    {
        world.JournalFaults.Clear();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                var synapses = await brain.Query(name).ReadSynapses();
                Assert.DoesNotContain(synapses,
                    synapse => synapse.Target == NeuronId.Plain(target) && synapse.SignalType == type);
                return;
            }
            catch (NeuronRecoveringException)
            {
                await Task.Delay(100);
            }
        }

        Assert.Fail($"Neuron '{name}' did not become readable within 10 seconds after storage recovered.");
    }

    [Then(@"""(.*)"" state does not contain ""(.*)""")]
    public async Task StateDoesNotContain(string name, string type)
        => Assert.DoesNotContain(await brain.Query(name).ReadState(), delivery => delivery.Signal.Type == type);

    [AfterScenario]
    [Scope(Feature = "Recovery")]
    public void AfterScenario() => world.JournalFaults.Clear();
}
