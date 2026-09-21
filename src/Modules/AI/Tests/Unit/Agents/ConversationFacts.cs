using DigitalBrain.AI;
using DigitalBrain.AI.Conversations;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ConversationFacts
{
    [Fact]
    public async Task CompletedTurnsReplayButStaleCompletionCannotReplaceNewRun()
    {
        await using var brain = await UnitTest.Create().WithModule<AIModule>().StartAsync(TestContext.Current.CancellationToken);
        var conversation = brain.Get<IConversation>("conversation");
        var first = await conversation.Begin("one", "hello", 0, "runtime");
        Assert.Equal(first.Revision, (await conversation.Begin("one", "hello", 0, "runtime")).Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.Begin("one", "different", 1, "runtime"));
        await conversation.Interrupt("one");
        var next = await conversation.Begin("two", "again", 2, "runtime");
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.Complete(new("one", "hello", "late", [])));
        Assert.Equal("two", (await conversation.Interrupt("one")).ActiveRunId);
        var done = await conversation.Complete(new("two", "again", "answer", ["window"]));
        Assert.Null(done.ActiveRunId);
        Assert.Single(done.Turns);
        Assert.Single((await conversation.Begin("two", "again", done.Revision, "runtime")).Turns);
        await conversation.Begin("three", "interrupted by restart", done.Revision, "runtime");
        var recovered = await conversation.Begin("four", "after restart", done.Revision + 1, "new-runtime");
        Assert.Equal("four", recovered.ActiveRunId);
        Assert.Equal("window", Assert.Single(Assert.Single(recovered.Turns).ResultIds));
    }
}