using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class FeedTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Chat_post_appends_one_feed_record_with_source_and_revision()
    {
        var feed = Neuron("feed", "main");
        var before = await feed.ReadEventsAsync(0, 10000);

        var chatId = Guid.NewGuid().ToString("N");
        var chatKey = AddressKey("chat", chatId);
        var chat = Neuron("chat", chatId);
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"hello"}""", "cmd-1", OwnerSession));

        var after = await feed.ReadEventsAsync(before.NextRevision, 10);
        Assert.Single(after.Events);
        Assert.Equal("feed.record", after.Events[0].Kind);
        Assert.Contains(chatKey, after.Events[0].PayloadJson);
        Assert.Contains("\"revision\":1", after.Events[0].PayloadJson);
    }

    [Fact]
    public async Task Two_chat_posts_produce_two_records_newest_first()
    {
        var feed = Neuron("feed", "main");
        var before = await feed.ReadEventsAsync(0, 10000);

        var chatId = Guid.NewGuid().ToString("N");
        var chatKey = AddressKey("chat", chatId);
        var chat = Neuron("chat", chatId);
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"one"}""", "cmd-1", OwnerSession));
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"two"}""", "cmd-2", OwnerSession));

        var after = await feed.ReadEventsAsync(before.NextRevision, 10);
        Assert.Equal(2, after.Events.Length);
        Assert.Contains(chatKey, after.Events[0].PayloadJson);
        Assert.Contains("\"revision\":1", after.Events[0].PayloadJson);
        Assert.Contains(chatKey, after.Events[1].PayloadJson);
        Assert.Contains("\"revision\":2", after.Events[1].PayloadJson);

        var snapshot = await feed.ReadAsync("recent");
        using var doc = JsonDocument.Parse(snapshot.StateJson);
        var records = doc.RootElement.GetProperty("records");
        Assert.Contains("\"revision\":2", records[0].GetRawText());
        Assert.Contains(chatKey, records[0].GetRawText());
        Assert.Contains("\"revision\":1", records[1].GetRawText());
        Assert.Contains(chatKey, records[1].GetRawText());
    }

    [Fact]
    public async Task Feed_neuron_does_not_self_append()
    {
        var feed = Neuron("feed", Guid.NewGuid().ToString("N"));
        var payload = JsonSerializer.Serialize(new { sourceKey = AddressKey("chat", "fake"), revision = 5, kind = "chat" });
        await feed.InvokeAsync(new("feed.append.v1", payload, "cmd-1", OwnerSession));

        var events = await feed.ReadEventsAsync(0, 10);
        Assert.Single(events.Events);
        Assert.Equal("feed.record", events.Events[0].Kind);
    }
}
