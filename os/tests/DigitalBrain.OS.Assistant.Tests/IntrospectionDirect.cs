using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Introspection;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Assistant.Tests;

public sealed class IntrospectionDirect(OSBehaviorsFixture fixture)
{
    private const int RequestTimeout = 60_000;
    private const string UserMessagedType = "DigitalBrain.Chat.UserMessaged";

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
    }
}
