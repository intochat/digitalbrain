using System.Globalization;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Introspection;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class IntrospectionDirect(OSBehaviorsFixture fixture)
{
    private const int RequestTimeout = 60_000;
    private const string UserMessagedType = "DigitalBrain.Chat.UserMessaged";
    private const string IntrospectionGrainType = "introspection";
    private const string DefaultInstance = "default";

    [Fact(Timeout = RequestTimeout, DisplayName =
        "a directed tally request counts the owner's chat facts by synapse type")]
    public async Task DirectedTallyCountsChatFacts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "direct-tally";

        test.Chat().Reply("Noted.");
        await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), "hello"));

        var tallied = await test.Client.Get<IIntrospection>()
            .SendAsync(new TallyJournalRequest("chat", chatName), cancellationToken);

        Assert.Null(tallied.Error);
        Assert.Equal(JournalDirection.Outgoing, tallied.Direction);
        Assert.Contains(
            tallied.Tallies,
            tally => tally.SynapseType == UserMessagedType && tally.Recorded == 1);
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "introspection tallies its own journal while it is occupied handling the request that asked")]
    public async Task IntrospectionTalliesItselfWhileHandlingTheRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);

        var tallied = await test.Client.Get<IIntrospection>()
            .SendAsync(new TallyJournalRequest(IntrospectionGrainType, DefaultInstance), cancellationToken);

        Assert.Null(tallied.Error);
        Assert.Equal(NeuronId.For<IIntrospection>(test.Client.Owner, DefaultInstance), tallied.Subject);
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "a directed journal page carries causal facts only: synapse type, caller and correlation, never payload text")]
    public async Task DirectedJournalPageCarriesCausalFactsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "direct-page";
        const string secret = "unmistakable-owner-text";

        test.Chat().Reply("Noted.");
        await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), secret));

        var page = await test.Client.Get<IIntrospection>()
            .SendAsync(
                new ReadJournalRequest("chat", chatName, JournalDirection.Outgoing, afterSequence: 0, maxEntries: 10, CommandId.New()),
                cancellationToken);

        Assert.Null(page.Error);
        Assert.False(page.Compacted);
        Assert.Contains(page.Entries, entry => entry.Synapse == nameof(UserMessaged));
        Assert.All(page.Entries, entry => Assert.DoesNotContain(secret, entry.Synapse, StringComparison.Ordinal));
        Assert.All(page.Entries, entry => Assert.NotEqual(0, entry.Sequence));
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "paging a journal in slices resumes at the last entry handed over, so no fact is stepped over")]
    public async Task TruncatedPageResumesAtItsLastEntry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string chatName = "direct-paging";
        const int sliceSize = 2;

        foreach (var message in new[] { "one", "two", "three" })
        {
            test.Chat().Reply($"Noted: {message}");
            await test.Client.GetGrainProxy<IChat>(chatName).Send(new SendMessage(CommandId.New(), message));
        }

        var whole = await ReadPageAsync(test, chatName, afterSequence: 0, ReadJournalRequest.MaximumMaxEntries, cancellationToken);
        Assert.Null(whole.Error);
        Assert.True(
            whole.Entries.Count > sliceSize,
            $"The journal holds {whole.Entries.Count} entries, so a page of {sliceSize} would not truncate.");

        var paged = new List<long>();
        long cursor = 0;
        for (var slice = 0; slice < whole.Entries.Count + 1; slice++)
        {
            var page = await ReadPageAsync(test, chatName, cursor, sliceSize, cancellationToken);
            Assert.Null(page.Error);
            if (page.Entries.Count == 0)
            {
                break;
            }

            paged.AddRange(page.Entries.Select(static entry => entry.Sequence));
            Assert.Equal(page.Entries[^1].Sequence, page.ResumeSequence);
            cursor = page.ResumeSequence;
        }

        Assert.Equal(whole.Entries.Select(static entry => entry.Sequence), paged);
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "an unknown neuron type is refused as a typed reply, not thrown into the outbox")]
    public async Task UnknownNeuronTypeIsRefusedWithoutOutboxChurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var introspection = test.Neuron<IIntrospection>();

        var tallied = await test.Client.Get<IIntrospection>()
            .SendAsync(new TallyJournalRequest("banana", "main"), cancellationToken);

        Assert.NotNull(tallied.Error);
        Assert.Contains("No neuron of type 'banana'", tallied.Error, StringComparison.Ordinal);
        Assert.Empty(tallied.Tallies);

        var delivered = await introspection.Incoming.ReadAsync<TallyJournalRequest>(
            cancellationToken: cancellationToken);
        Assert.Single(delivered);
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "an unknown neuron name is refused without activating the neuron that was asked about")]
    public async Task UnknownNeuronNameIsRefusedWithoutActivatingIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        const string ghost = "never-opened";

        test.Chat().Reply("Noted.");
        await test.Client.GetGrainProxy<IChat>("direct-ghost").Send(new SendMessage(CommandId.New(), "hello"));

        var tallied = await test.Client.Get<IIntrospection>()
            .SendAsync(new TallyJournalRequest("chat", ghost), cancellationToken);

        Assert.NotNull(tallied.Error);
        Assert.Contains("never activates a neuron", tallied.Error, StringComparison.Ordinal);

        var topology = await test.Client.Get<IIntrospection>()
            .SendAsync(new ReadTopologyRequest(), cancellationToken);

        Assert.DoesNotContain(
            topology.Neurons,
            neuron => neuron.Identity.EndsWith($"/{ghost}", StringComparison.Ordinal));
    }

    [Fact(DisplayName =
        "an introspection journal read gives up inside one outbox delivery attempt, not at the grain call's five-minute response timeout")]
    public void JournalReadGivesUpInsideOneDeliveryAttempt()
    {
        // Strictly less, not equal: TryDeliverAsync arms the outer attemptCts.CancelAfter
        // (DeliveryAttemptTimeout) before Deliver even starts, so a bound equal to that outer
        // deadline always loses the race to it - the read would see OperationCanceledException,
        // never the TimeoutException this bound exists to surface as a typed refusal.
        Assert.True(
            IntrospectionNeuron.JournalReadBound < DeliveryPolicy.DeliveryAttemptTimeout,
            $"A read bound of {IntrospectionNeuron.JournalReadBound} does not come in strictly under "
            + $"the outer {DeliveryPolicy.DeliveryAttemptTimeout} delivery-attempt deadline armed "
            + "before this handler's turn starts, so the timeout catch can never win that race.");
        Assert.True(
            IntrospectionNeuron.JournalReadBound < TimeSpan.Parse(NeuronCallTimeouts.LongRunning, CultureInfo.InvariantCulture),
            $"A read bound of {IntrospectionNeuron.JournalReadBound} gives up no sooner than the "
            + $"{NeuronCallTimeouts.LongRunning} response timeout it exists to tighten.");
    }

    [Fact(Timeout = RequestTimeout, DisplayName =
        "a journal read bounded strictly under the outbox's outer delivery-attempt deadline reds a TimeoutException, not the deadline's bare cancellation")]
    public async Task InnerReadBoundWinsTheRaceAgainstTheOuterDeliveryDeadline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Mirrors Neuron.Outbox.TryDeliverAsync exactly: the outer attempt deadline is armed before
        // the inner call ever starts. A subject that never answers must still surface as a
        // TimeoutException from the inner WaitAsync, not as OperationCanceledException from the
        // outer token - that distinction is what makes IntrospectionNeuron.TryReadAsync's typed
        // refusal reachable at all.
        using var outerAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        outerAttempt.CancelAfter(DeliveryPolicy.DeliveryAttemptTimeout);

        var neverAnswers = new TaskCompletionSource<JournalRead>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAsync<TimeoutException>(
            () => neverAnswers.Task.WaitAsync(IntrospectionNeuron.JournalReadBound, outerAttempt.Token));

        Assert.False(outerAttempt.IsCancellationRequested);
    }

    [Fact(DisplayName = "a journal page request refuses a page size outside the bounds the description advertises")]
    public void JournalPageSizeIsBounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadJournalRequest(
            "chat",
            "main",
            JournalDirection.Outgoing,
            afterSequence: 0,
            maxEntries: ReadJournalRequest.MaximumMaxEntries + 1,
            CommandId.New()));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ReadJournalRequest(
            "chat",
            "main",
            JournalDirection.Outgoing,
            afterSequence: -1,
            maxEntries: ReadJournalRequest.DefaultMaxEntries,
            CommandId.New()));

        var unrecognised = Assert.Throws<ArgumentException>(() => new TallyJournalRequest(
            "chat",
            "main",
            "sideways",
            CommandId.New()));
        Assert.Contains("incoming", unrecognised.Message, StringComparison.Ordinal);

        var unaddressable = Assert.Throws<ArgumentException>(
            () => new TallyJournalRequest("chat", "other-owner/main"));
        Assert.Contains("not addressable", unaddressable.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new ReadJournalRequest("chat neuron", "main"));
    }

    private static Task<JournalPageRead> ReadPageAsync(
        TestBrain test,
        string chatName,
        long afterSequence,
        int maxEntries,
        CancellationToken cancellationToken)
        => test.Client.Get<IIntrospection>().SendAsync(
            new ReadJournalRequest(
                "chat",
                chatName,
                JournalDirection.Outgoing,
                afterSequence,
                maxEntries,
                CommandId.New()),
            cancellationToken);
}
