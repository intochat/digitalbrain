using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Sdk;
using Brain.UiGateway;
using Xunit;

namespace Brain.KernelTests;

public class UiGatewayTests(BrainClusterFixture<WorkspaceKindsConfigurator> fixture)
    : BrainTest<WorkspaceKindsConfigurator>(fixture)
{
    private static string UiChatAddress(string id) => new NeuronAddress("local-owner", "actor/ui-dev", $"chat/{id}").ToGrainKey();

    [Fact]
    public async Task Invoke_chat_post_returns_receipt_at_revision_one()
    {
        var address = UiChatAddress(Guid.NewGuid().ToString("N"));
        var receipt = await UiEndpoints.InvokeAsync(
            Cluster.Client, UiEndpoints.DevCallerKey, address, "chat.post.v1", """{"text":"hello"}""", "cmd-1", null);

        Assert.Equal(1, receipt.Revision);
        Assert.Equal("cmd-1", receipt.CommandId);
    }

    [Fact]
    public async Task Invoke_replays_same_receipt_for_duplicate_command_id()
    {
        var address = UiChatAddress(Guid.NewGuid().ToString("N"));
        var first = await UiEndpoints.InvokeAsync(
            Cluster.Client, UiEndpoints.DevCallerKey, address, "chat.post.v1", """{"text":"hello"}""", "cmd-dup", null);
        var replay = await UiEndpoints.InvokeAsync(
            Cluster.Client, UiEndpoints.DevCallerKey, address, "chat.post.v1", """{"text":"ignored"}""", "cmd-dup", null);

        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task Read_and_describe_pass_through_to_the_neuron()
    {
        var address = AddressKey("catalog", "main");

        var snapshot = await UiEndpoints.ReadAsync(Cluster.Client, address, "");
        Assert.Contains("\"kind\":\"chat\"", snapshot.StateJson);

        var description = await UiEndpoints.DescribeAsync(Cluster.Client, address);
        Assert.Equal("catalog", description.Kind);
    }

    [Fact]
    public void ToErrorPayload_maps_brain_exception_to_code_and_detail()
    {
        var exception = new BrainException(BrainErrors.UnknownContract, "bad contract");
        var json = JsonSerializer.Serialize(UiEndpoints.ToErrorPayload(exception));

        Assert.Contains("\"code\":\"contract.unknown\"", json);
        Assert.Contains("\"detail\":\"bad contract\"", json);
    }

    [Fact]
    public void WatchPager_maps_feed_records_skips_other_events_and_advances_cursor()
    {
        var page = new NeuronEventPage(
            [
                new NeuronEvent(1, "feed.record", """{"sourceKey":"owner|actor/test|chat/x","revision":1,"kind":"chat"}""", "cmd-1", DateTimeOffset.UtcNow),
                new NeuronEvent(2, "chat.message", """{"text":"not a feed record"}""", "cmd-2", DateTimeOffset.UtcNow),
                new NeuronEvent(3, "feed.record", """{"sourceKey":"owner|actor/test|chat/y","revision":2,"kind":"chat"}""", "cmd-3", DateTimeOffset.UtcNow)
            ],
            NextRevision: 3);

        var frames = WatchPager.NextFrames(page);

        Assert.Equal(2, frames.Count);
        Assert.Contains("\"sequence\":1", frames[0]);
        Assert.Contains("chat/x", frames[0]);
        Assert.Contains("\"sequence\":3", frames[1]);
        Assert.Contains("chat/y", frames[1]);
        Assert.Equal(3, WatchPager.NextCursor(page));
    }
}
