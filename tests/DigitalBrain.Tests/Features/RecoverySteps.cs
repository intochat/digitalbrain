using System.Diagnostics;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class RecoverySteps(BrainWorld world, BrainSteps brain)
{
    private Exception? _readError;
    private Exception? _connectError;
    private string? _interleavingNeuron;

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

    [When(@"""(.*)"" connects ""(.*)"" to plain ""(.*)"" for ""(.*)"" and a read interleaves during the recovery")]
    public async Task ConnectWithInterleavingRead(string _, string from, string target, string type)
    {
        var neuron = brain.Neuron(from);
        // Activate before arming the hold so only the recovery reload can claim it.
        await neuron.ReadPendingCount().WaitAsync(TimeSpan.FromSeconds(10));
        world.JournalFaults.HoldNextRead();
        var connect = Task.Run(() => neuron.Connect(NeuronId.Plain(target), type));
        try
        {
            await world.JournalFaults.ReadHeld.WaitAsync(TimeSpan.FromSeconds(10));
            _interleavingNeuron = from;
            _readError = await Record.ExceptionAsync(() => neuron.ReadPendingCount().WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            world.JournalFaults.ReleaseRead();
            _connectError = await Record.ExceptionAsync(() => connect.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    [Then(@"the interleaving read of ""(.*)"" threw NeuronRecovering")]
    public void InterleavingReadRefused(string name)
    {
        Assert.Equal(name, _interleavingNeuron);
        Assert.IsType<NeuronRecoveringException>(_readError);
    }

    [Then("the connect failed")]
    public void ConnectFailed()
    {
        var error = _connectError is AggregateException aggregate
            ? Assert.Single(aggregate.Flatten().InnerExceptions)
            : _connectError;
        Assert.IsType<NeuronPersistenceException>(error);
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
