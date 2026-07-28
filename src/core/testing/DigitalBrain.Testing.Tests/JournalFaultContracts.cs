using DigitalBrain.Abstractions;
using DigitalBrain.Quickstart;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class JournalFaultContracts(TestingFixture fixture)
{
    private const string BrainCleanupOperation = "brain.cleanup";
    private const string UnconsumedJournalFaultsRemain =
        "Unconsumed journal commit faults remain";
    private const string SessionCommitFailure = "session journal commit failure";
    private const string NeverFiredFault = "never fired";
    private const string DisarmedWithoutFire = "disarmed without fire";

    [Fact(DisplayName = "FailNextJournalCommit fails the next journal write for the target neuron")]
    public async Task FailNextJournalCommitFailsTheNextJournalWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var session = test.Neuron<ISessionNeuron>(TestingScenario.Session);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);

        await using (var fault = session.FailNextJournalCommit(SessionCommitFailure))
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => test.Client.SendAsync<IGreeter>(greeter.Id.Name, new SayHello(TestingScenario.Guest)));
            Assert.Equal(SessionCommitFailure, failure.Message);
        }

        await test.Client.SendAsync<IGreeter>(greeter.Id.Name, new SayHello(TestingScenario.Guest));
        var greeted = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal(TestingScenario.GreetedMessage(TestingScenario.Guest), greeted.Synapse.Message);
    }

    [Fact(DisplayName = "An unconsumed journal fault fails TestBrain dispose with brain.cleanup diagnostics")]
    public async Task UnconsumedJournalFaultFailsBrainDispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);
        _ = greeter.FailNextJournalCommit(NeverFiredFault);

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(async () => await test.DisposeAsync());

        Assert.Contains(BrainCleanupOperation, failure.Message, StringComparison.Ordinal);
        var leak = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains(UnconsumedJournalFaultsRemain, leak.Message, StringComparison.Ordinal);
        Assert.Contains(NeverFiredFault, leak.Message, StringComparison.Ordinal);
        Assert.Contains(greeter.Id.ToString(), leak.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Disposing an armed journal fault handle disarms it so brain dispose stays clean")]
    public async Task DisposingAnArmedJournalFaultHandleAllowsCleanBrainDispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);

        await using (greeter.FailNextJournalCommit(DisarmedWithoutFire))
        {
        }

        await test.Client.SendAsync<IGreeter>(greeter.Id.Name, new SayHello(TestingScenario.Guest));
        var greeted = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal(TestingScenario.GreetedMessage(TestingScenario.Guest), greeted.Synapse.Message);
    }
}
