using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class JournalContracts(TestingFixture fixture)
{
    private const int EvidenceLimit = 64;
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ATestNeuronUsesTheRealReferenceAndCommittedJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("primary");

        await test.Client.SendAsync<IEchoNeuron>(
            "primary",
            new EchoRequested("Ada"));

        var observed = await echo.Outgoing.NextAsync<Echoed>(cancellationToken);

        Assert.Equal("direct", await echo.Reference.Echo("direct"));
        Assert.Equal(
            "client",
            await test.Client.Get<IEchoNeuron>("primary").Echo("client"));
        Assert.Equal("Ada", observed.Synapse.Value);
        Assert.Equal(echo.Id, observed.Subject);
        Assert.Equal(echo.Id, observed.Caller);
        Assert.Equal(JournalKind.Outgoing, observed.Direction);
        Assert.True(observed.Sequence > 0);
    }

    [Fact]
    public async Task ReadAsyncReturnsTypedCommittedDeliveries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("reader");
        var committed = echo.Outgoing.NextAsync<Echoed>(cancellationToken);

        await test.Client.SendAsync<IEchoNeuron>(
            "reader",
            new EchoRequested("Grace"));
        await committed;

        var incoming =
            await echo.Incoming.ReadAsync<EchoRequested>(
                cancellationToken: cancellationToken);
        var outgoing =
            await echo.Outgoing.ReadAsync<Echoed>(
                cancellationToken: cancellationToken);

        Assert.Equal("Grace", Assert.Single(incoming).Synapse.Value);
        Assert.Equal("Grace", Assert.Single(outgoing).Synapse.Value);
    }

    [Fact]
    public async Task RepeatedHandlesReuseTheirJournalInfrastructure()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        var first = test.Neuron<IEchoNeuron>("shared");
        var second = test.Owner("default").Neuron<IEchoNeuron>("shared");

        Assert.Equal(first.Id, second.Id);
        Assert.Same(first.Incoming, second.Incoming);
        Assert.Same(first.Outgoing, second.Outgoing);
    }

    [Fact]
    public async Task ConcurrentNextCallsEachReceiveACommittedDelivery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("concurrent");
        var first = echo.Outgoing.NextAsync<Echoed>(cancellationToken);
        var second = echo.Outgoing.NextAsync<Echoed>(cancellationToken);

        await test.Client.SendAsync<IEchoNeuron>(
            "concurrent",
            new EchoRequested("first"));
        await test.Client.SendAsync<IEchoNeuron>(
            "concurrent",
            new EchoRequested("second"));

        var firstObserved = await first;
        var secondObserved = await second;

        Assert.Equal("first", firstObserved.Synapse.Value);
        Assert.Equal("second", secondObserved.Synapse.Value);
        Assert.True(firstObserved.Sequence < secondObserved.Sequence);
    }

    [Fact]
    public async Task AResetSnapshotReportsCompactionEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("compacted");
        var committed = echo.Outgoing.NextAsync<Echoed>(cancellationToken);

        await test.Client.SendAsync<IEchoNeuron>(
            "compacted",
            new EchoRequested("retained"));
        var observed = await committed;

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => echo.Outgoing.ReadAsync<Echoed>(
                long.MaxValue,
                cancellationToken));
        var journal = Assert.IsType<InvalidOperationException>(
            failure.InnerException);

        Assert.Contains(echo.Id.ToString(), journal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(JournalKind.Outgoing), journal.Message, StringComparison.Ordinal);
        Assert.Contains(
            long.MaxValue.ToString(CultureInfo.InvariantCulture),
            journal.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            observed.Sequence.ToString(CultureInfo.InvariantCulture),
            journal.Message,
            StringComparison.Ordinal);
        Assert.Contains("retained=1", journal.Message, StringComparison.Ordinal);
        Assert.Contains("dropped=0", journal.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Echoed).FullName!, journal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JournalCleanupCompletesBeforeTheNextMethodLease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var first = await fixture.CreateBrainAsync(cancellationToken))
        {
            var echo = first.Neuron<IEchoNeuron>("cleanup");
            var committed = echo.Outgoing.NextAsync<Echoed>(cancellationToken);

            await first.Client.SendAsync<IEchoNeuron>(
                "cleanup",
                new EchoRequested("observed"));
            await committed;
        }

        await using var second = await fixture.CreateBrainAsync(cancellationToken);

        Assert.NotNull(second.Neuron<IEchoNeuron>("after-cleanup").Reference);
    }

    [Fact]
    public async Task EvidenceOverflowDoesNotBlockProductSendsAndFailsTheWait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("overflow");
        var installed = echo.Outgoing.NextAsync<Echoed>(cancellationToken);

        await echo.Reference.Publish("installed");
        await installed;

        for (var index = 0; index <= EvidenceLimit; index++)
        {
            await echo.Reference
                .Publish($"overflow-{index}")
                .WaitAsync(DeadlockGuard, cancellationToken);
        }

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => echo.Outgoing
                .NextAsync<EchoRequested>(cancellationToken)
                .WaitAsync(DeadlockGuard, cancellationToken));
        var journal = Assert.IsType<InvalidOperationException>(
            failure.InnerException);

        Assert.Contains("overflow", journal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(echo.Id.ToString(), journal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(JournalKind.Outgoing), journal.Message, StringComparison.Ordinal);
        Assert.Contains(
            EvidenceLimit.ToString(CultureInfo.InvariantCulture),
            journal.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnIncompatibleHistoricalBatchCannotExceedTheEvidenceLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("historical-overflow");

        for (var index = 0; index <= EvidenceLimit; index++)
        {
            await echo.Reference.Publish($"historical-{index}");
        }

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => echo.Outgoing
                .NextAsync<EchoRequested>(cancellationToken)
                .WaitAsync(DeadlockGuard, cancellationToken));
        var journal = Assert.IsType<InvalidOperationException>(
            failure.InnerException);

        Assert.Contains("overflow", journal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(echo.Id.ToString(), journal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(JournalKind.Outgoing), journal.Message, StringComparison.Ordinal);
        Assert.Contains(
            EvidenceLimit.ToString(CultureInfo.InvariantCulture),
            journal.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposalTerminatesAnOutstandingWaitBeforeReleasingTheLease()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first =
            await fixture.CreateBrainAsync(cancellationToken);
        var echo = first.Neuron<IEchoNeuron>("dispose-wait");
        var waiting =
            echo.Outgoing.NextAsync<EchoRequested>(cancellationToken);

        await echo.Reference.Publish("unmatched");
        Assert.False(waiting.IsCompleted);

        await first
            .DisposeAsync()
            .AsTask()
            .WaitAsync(DeadlockGuard, cancellationToken);

        Assert.True(waiting.IsCompleted);
        await Assert.ThrowsAnyAsync<Exception>(() => waiting);

        await using var second = await fixture
            .CreateBrainAsync(cancellationToken)
            .WaitAsync(DeadlockGuard, cancellationToken);

        Assert.NotNull(second.Neuron<IEchoNeuron>("after-wait").Reference);
    }
}
