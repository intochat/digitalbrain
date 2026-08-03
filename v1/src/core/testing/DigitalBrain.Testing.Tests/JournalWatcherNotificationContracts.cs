using DigitalBrain.Abstractions;
using DigitalBrain.TestingTests.Harness;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class JournalWatcherNotificationContracts(TestingFixture fixture)
{
    private static readonly TimeSpan HangBudget = TimeSpan.FromSeconds(5);

    [Fact(DisplayName =
        "emitting a fact does not hang when a journal observer reenters the emitting neuron during ObserveAsync")]
    public async Task EmitDoesNotHangWhenJournalObserverReentersTheEmittingNeuron()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);
        var probe = test.Neuron<IJournalHangProbe>("hang-probe");

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hang.CancelAfter(HangBudget);

        await probe.Reference.EmitWhileObserverReenters(
                greeter.Id.Name,
                "reenter-watcher",
                TestingScenario.Guest)
            .WaitAsync(hang.Token);

        await greeter.Outgoing.NextAsync<Greeted>(cancellationToken)
            .WaitAsync(HangBudget, cancellationToken);

        using var reentryWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reentryWait.CancelAfter(HangBudget);
        while (await probe.Reference.Reentries("reenter-watcher") == 0)
        {
            await Task.Delay(20, reentryWait.Token);
        }
    }

    [Fact(DisplayName =
        "emitting a fact does not hang when a client journal observer never completes ObserveAsync")]
    public async Task EmitDoesNotHangWhenClientJournalObserverNeverCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);
        var probe = test.Neuron<IJournalHangProbe>("hang-probe");

        var stuck = new NeverCompletingJournalObserver();
        var reference = test.Cluster.Client.CreateObjectReference<IJournalObserver>(stuck);
        try
        {
            using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            hang.CancelAfter(HangBudget);

            await probe.Reference.EmitWhileObserverIsStuck(
                    greeter.Id.Name,
                    TestingScenario.Guest,
                    reference)
                .WaitAsync(hang.Token);

            await greeter.Outgoing.NextAsync<Greeted>(cancellationToken)
                .WaitAsync(HangBudget, cancellationToken);
        }
        finally
        {
            var session = test.Cluster.Client.GetGrain<ISessionNeuron>(
                ISessionNeuron.ForOwner(greeter.Id.Owner).ToGrainId());
            await session.UnwatchNeuron(greeter.Id, reference);
            test.Cluster.Client.DeleteObjectReference<IJournalObserver>(reference);
            stuck.Release();
        }
    }

    private sealed class NeverCompletingJournalObserver : IJournalObserver
    {
        private readonly TaskCompletionSource _hold = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ObserveAsync(JournalKind kind, JournalRead read)
        {
            ArgumentNullException.ThrowIfNull(read);
            return _hold.Task;
        }

        public void Release() => _hold.TrySetResult();
    }
}
