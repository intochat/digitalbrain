using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Chat;
using DigitalBrain.Flutter.Chat.Signals;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace DigitalBrain.Modules.Flutter.Tests.Unit.Chat;

public sealed class ChatFacts
{
    [Fact]
    public async Task SubmittingTheDraftClearsItAndAnnouncesTheText()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var chat = brain.Get<IChat>("room");
        await chat.Configure("Message");
        await using var submitted = await brain.Observe<ChatSubmitted>(chat, ct);
        await chat.SetDraft("  hello ");

        await chat.Submit();

        Assert.Equal(new ChatSubmitted("room", "hello"), await submitted.NextAsync(ct: ct));
        var state = await chat.Read();
        Assert.Equal("", state.Draft);
        Assert.Equal("Message", state.Label);
        await Assert.ThrowsAsync<InvalidOperationException>(chat.Submit);
    }

    [Fact]
    public async Task PostedMessagesKeepTheirOrderAndEachChangeIsAnnounced()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<FlutterModule>().StartAsync(ct);
        var chat = brain.Get<IChat>("room");
        await using var changed = await brain.Observe<ChatChanged>(chat, ct);

        await chat.Post(ChatRole.User, "hi");
        await chat.Post(ChatRole.Assistant, "hello");

        Assert.Equal(1, (await changed.NextAsync(ct: ct)).Revision);
        Assert.Equal(2, (await changed.NextAsync(ct: ct)).Revision);
        var messages = (await chat.Read()).Messages;
        Assert.Equal([(ChatRole.User, "hi"), (ChatRole.Assistant, "hello")], messages.Select(message => (message.Role, message.Text)));
        await Assert.ThrowsAsync<ArgumentException>(() => chat.Post(ChatRole.User, " "));
    }
}
