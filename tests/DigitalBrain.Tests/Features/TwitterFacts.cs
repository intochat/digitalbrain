using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using DigitalBrain.Twitter;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TwitterFacts
{
    [Fact]
    public async Task Receipt_schedules_one_post_and_provider_duplicates_do_not_publish_again()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TwitterModule)]) });
        var source = Source(brain);
        var command = new Post(CommandId.New(), "post-42", "@ElonMusk", "A test receipt");
        var accepted = await source.Accept(command);
        Assert.Equal("post-42", accepted.Receipt.EventId);
        Assert.NotEqual(default, accepted.Work);
        Assert.Equal(accepted, await source.Accept(command));
        var signal = await ReactionWait.ForSignalAsync(source, "Posted", TestContext.Current.CancellationToken);
        Assert.Equal(new Posted("post-42", "elonmusk", "A test receipt"), signal.Body(TwitterJson.Default.Posted));
        await source.Accept(command with { Id = CommandId.New() });
        await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        Assert.Equal(1, (await source.Read()).PublishedCount);
        Assert.Single((await source.ReadJournal(JournalKind.Outgoing, 0)).Delta, item => item.Signal.Type == "Posted");
    }

    [Fact]
    public async Task Wrong_account_is_rejected_before_receipt_acceptance()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TwitterModule)]) });
        var source = Source(brain);
        await Assert.ThrowsAsync<CommandRejectedException>(() => source.Accept(new Post(CommandId.New(), "42", "someoneelse", "text")));
        Assert.Equal(0, await source.ReadPendingCount());
        Assert.Equal(0, (await source.Read()).PublishedCount);
    }

    [Fact]
    public async Task Deduplication_and_last_post_survive_cold_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-twitter", Guid.NewGuid().ToString("N"));
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TwitterModule)]), PersistenceDirectory = directory }))
        {
            var source = Source(brain);
            await source.Accept(new Post(CommandId.New(), "persisted", "elonmusk", "Durable receipt"));
            await ReactionWait.ForSignalAsync(source, "Posted", TestContext.Current.CancellationToken);
            await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
        }

        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(TwitterModule)]), PersistenceDirectory = directory }))
        {
            var source = Source(brain);
            Assert.Equal("persisted", (await source.Read()).LastPost?.EventId);
            await source.Accept(new Post(CommandId.New(), "persisted", "elonmusk", "Redelivery"));
            await ReactionWait.UntilAsync(async () => await source.ReadPendingCount() == 0, TestContext.Current.CancellationToken);
            Assert.Equal(1, (await source.Read()).PublishedCount);
            Assert.Single((await source.ReadJournal(JournalKind.Outgoing, 0)).Delta, item => item.Signal.Type == "Posted");
        }
    }

    private static ITwitter Source(BrainSimulation brain) => brain.Grains.GetGrain<ITwitter>(new NeuronId("twitter", "elonmusk").ToGrainId());
}
