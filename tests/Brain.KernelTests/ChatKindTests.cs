using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class ChatKindTests(BrainClusterFixture<ChatKindsConfigurator> fixture)
    : BrainTest<ChatKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Posts_fold_into_conversation_projection()
    {
        var chat = Neuron("chat", "main");
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"hello"}""", "cmd-1", OwnerSession));
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"world"}""", "cmd-2", OwnerSession));
        var snapshot = await chat.ReadAsync("conversation");
        Assert.Equal(2, snapshot.Revision);
        Assert.Contains("hello", snapshot.StateJson);
        Assert.Contains("world", snapshot.StateJson);
    }

    [Fact]
    public async Task Empty_text_fails_closed()
    {
        var chat = Neuron("chat", "guard");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("chat.post.v1", """{"text":""}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(0, (await chat.ReadAsync("conversation")).Revision);
    }
}
