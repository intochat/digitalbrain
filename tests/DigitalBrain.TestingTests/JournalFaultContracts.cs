using DigitalBrain.Abstractions;
using DigitalBrain.Quickstart;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class JournalFaultContracts(TestingFixture fixture)
{
    [Fact(DisplayName = "FailNextJournalCommit fails the next journal write for the target neuron")]
    public async Task FailNextJournalCommitFailsTheNextJournalWrite()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var session = test.Neuron<ISessionNeuron>("session");
        var greeter = test.Neuron<IGreeter>("welcome");

        await using (var fault = session.FailNextJournalCommit("session journal commit failure"))
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada")));
            Assert.Equal("session journal commit failure", failure.Message);
        }

        await test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));
        var greeted = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal("Hello, Ada.", greeted.Synapse.Message);
    }

    [Fact(DisplayName = "An unconsumed journal fault fails TestBrain dispose with brain.cleanup diagnostics")]
    public async Task UnconsumedJournalFaultFailsBrainDispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>("welcome");
        _ = greeter.FailNextJournalCommit("never fired");

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            async () => await test.DisposeAsync());

        Assert.Contains(
            "brain.cleanup",
            failure.Message,
            StringComparison.Ordinal);
        var leak = Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Contains(
            "Unconsumed journal commit faults remain",
            leak.Message,
            StringComparison.Ordinal);
        Assert.Contains("never fired", leak.Message, StringComparison.Ordinal);
        Assert.Contains(greeter.Id.ToString(), leak.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Disposing an armed journal fault handle disarms it so brain dispose stays clean")]
    public async Task DisposingAnArmedJournalFaultHandleAllowsCleanBrainDispose()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>("welcome");

        await using (greeter.FailNextJournalCommit("disarmed without fire"))
        {
        }

        await test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));
        var greeted = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal("Hello, Ada.", greeted.Synapse.Message);
    }
}
