using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class NotificationFacts
{
    [Fact]
    public async Task Publish_deduplicates_event_ids_and_dismissal_survives_cold_reload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-notifications", Guid.NewGuid().ToString("N"));
        NotificationEntry saved;
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]), PersistenceDirectory = directory }))
        {
            var notification = Inbox(brain);
            var command = new PublishNotification(CommandId.New(), "update:1", "Message", "<script>literal text</script>", "incoming");
            var accepted = await notification.Publish(command, TestContext.Current.CancellationToken);
            var retry = await notification.Publish(command, TestContext.Current.CancellationToken);
            Assert.Equal(accepted, retry);
            await notification.Publish(command with { Id = CommandId.New(), Message = "must not overwrite" }, TestContext.Current.CancellationToken);
            await Drain(notification);
            saved = Assert.Single((await notification.Read()).Items);
            Assert.Equal(command.Message, saved.Message);
            Assert.False(saved.Dismissed);
            Assert.True(saved.CreatedUnixSeconds > 0);
            await notification.Dismiss(new(CommandId.New(), "update:1"), TestContext.Current.CancellationToken);
            await notification.Dismiss(new(CommandId.New(), "update:1"), TestContext.Current.CancellationToken);
            await notification.Dismiss(new(CommandId.New(), "unknown"), TestContext.Current.CancellationToken);
            await Drain(notification);
            Assert.True(Assert.Single((await notification.Read()).Items).Dismissed);
        }
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]), PersistenceDirectory = directory }))
        {
            var notification = Inbox(brain);
            Assert.Equal(saved with { Dismissed = true }, Assert.Single((await notification.Read()).Items));
            await notification.Publish(new(CommandId.New(), "update:1", "Retry", "must not resurrect", "incoming"), TestContext.Current.CancellationToken);
            await Drain(notification);
            Assert.Equal(saved with { Dismissed = true }, Assert.Single((await notification.Read()).Items));
            Assert.Empty((await Inbox(brain, "telegram-2").Read()).Items);
        }
    }

    [Fact]
    public async Task Bounded_inbox_remembers_evicted_events_across_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-notifications", Guid.NewGuid().ToString("N"));
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]), PersistenceDirectory = directory }))
        {
            var notification = Inbox(brain);
            for (var index = 0; index <= NotificationState.MaxItems; index++)
            {
                await notification.Publish(new(CommandId.New(), $"event:{index}", "Message", "Body", "incoming"), TestContext.Current.CancellationToken);
            }
            await Drain(notification);
            var items = (await notification.Read()).Items;
            Assert.Equal(NotificationState.MaxItems, items.Count);
            Assert.DoesNotContain(items, item => item.EventId == "event:0");
        }
        await using (var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]), PersistenceDirectory = directory }))
        {
            var notification = Inbox(brain);
            await notification.Publish(new(CommandId.New(), "event:0", "Retry", "Body", "incoming"), TestContext.Current.CancellationToken);
            await Drain(notification);
            var items = (await notification.Read()).Items;
            Assert.Equal(NotificationState.MaxItems, items.Count);
            Assert.DoesNotContain(items, item => item.EventId == "event:0");
        }
    }

    [Fact]
    public async Task Invalid_publication_does_not_change_inbox()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        var notification = Inbox(brain);
        await Assert.ThrowsAsync<ArgumentException>(() => notification.Publish(new(CommandId.New(), " ", "Title", "Body", "incoming"), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => notification.Publish(new(CommandId.New(), "event", "Title", new string('x', 16385), "incoming"), TestContext.Current.CancellationToken));
        Assert.Empty((await notification.Read()).Items);
    }

    private static INotification Inbox(BrainSimulation brain, string name = "telegram-1")
        => brain.Grains.GetGrain<INotification>(new NeuronId(UIVocabulary.NotificationType, name).ToGrainId());

    private static Task Drain(INotification notification)
        => UiWait.Until(async () => await notification.ReadPendingCount() == 0);
}
