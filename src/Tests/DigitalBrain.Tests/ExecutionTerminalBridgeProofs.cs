using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Execution;
using DigitalBrain.Tests.Harness;
using Xunit;

namespace DigitalBrain.Tests;

[Collection(BrainCollection.Name)]
public sealed class ExecutionTerminalBridgeProofs(BrainClusterFixture fixture)
{
    [Fact]
    public async Task ExecutionNotifiesOriginOnSuccess()
    {
        var brain = fixture.BrainFor("exec-origin");
        var worker = new NeuronId("worker", brain.Owner, "origin-worker");
        var origin = NeuronId.For<IChat>(brain.Owner, "origin-chat");
        HarnessWorkerControl.Configure("origin-worker", HarnessWorkerScript.SucceedOnAccept);

        await brain.Get<IExecution>("origin-run").FireAsync(
            new ApplyExecution(
                CommandId.New(),
                new StartExecution(
                    new ProbeGoal("ping"),
                    worker,
                    new ExecutionPolicy(1, TimeSpan.FromSeconds(1), null),
                    RetryOf: null,
                    Origin: origin)),
            TestContext.Current.CancellationToken);

        await Journals.WaitForAsync(
            brain, origin, JournalKind.Incoming,
            delivery => delivery.Synapse is ExecutionTerminal t
                && t.State == ExecutionState.Succeeded,
            patience: TimeSpan.FromSeconds(45));

        var done = await brain.GetGrainProxy<IExecution>("origin-run").Read();
        Assert.Equal(ExecutionState.Succeeded, done.State);
    }
}
