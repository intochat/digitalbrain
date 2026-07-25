using DigitalBrain.AI.Ollama;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class OrchestrationL1(ModuleFixture fixture)
{
    [Fact(DisplayName = "Concurrent.Respond fans out to multiple scripted participants")]
    public async Task ConcurrentRespondFansOutToMultipleParticipants()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("concurrent-left");
        test.Chat().Reply("concurrent-right");

        var response = await test.Client.Get<IConcurrentProbe>("concurrent-team").Respond(
            [new ChatMessage(ChatRole.User, "fan out")]);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.True(
            response.Text.Contains("concurrent-left", StringComparison.Ordinal)
            || response.Text.Contains("concurrent-right", StringComparison.Ordinal),
            $"Expected a scripted participant reply in '{response.Text}'.");
        Assert.Equal(2, test.Chat().CallCount);
    }

    [Fact(DisplayName = "GroupChat.Respond runs multiple scripted participants")]
    public async Task GroupChatRespondRunsMultipleParticipants()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("group-left");
        test.Chat().Reply("group-right");

        var response = await test.Client.Get<IGroupChatProbe>("group-team").Respond(
            [new ChatMessage(ChatRole.User, "round robin")]);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.True(
            response.Text.Contains("group-left", StringComparison.Ordinal)
            || response.Text.Contains("group-right", StringComparison.Ordinal),
            $"Expected a scripted participant reply in '{response.Text}'.");
        Assert.Equal(2, test.Chat().CallCount);
    }

    [Fact(DisplayName = "GroupChat supervised Accept/Continue/Cancel throw until the Orleans path is built")]
    public async Task GroupChatSupervisedAttemptsThrow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var worker = test.Client.Get<IGroupChatProbe>("supervised-team");
        var workerId = test.Neuron<IGroupChatProbe>("supervised-team").Id;
        var taskId = test.Neuron<ILlama32>("task-placeholder").Id;
        var attempt = new AttemptId(Guid.NewGuid());
        var request = new AttemptRequest(
            taskId,
            workerId,
            attempt,
            Revision: 0,
            new ProbeGoal("supervised"));
        var cursor = new AttemptCursor(taskId, workerId, attempt, Revision: 0);

        var accept = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.InvokeAccept(request));
        var cont = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.InvokeContinue(cursor));
        var cancel = await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.InvokeCancel(cursor));

        Assert.Contains("supervised Attempts are not implemented", accept.Message, StringComparison.Ordinal);
        Assert.Contains("supervised Attempts are not implemented", cont.Message, StringComparison.Ordinal);
        Assert.Contains("supervised Attempts are not implemented", cancel.Message, StringComparison.Ordinal);
        Assert.Contains("Respond", accept.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "second Concurrent.Respond reuses the durable session on the same orchestration id")]
    public async Task SecondConcurrentRespondReusesDurableSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("session-a-left");
        test.Chat().Reply("session-a-right");
        test.Chat().Reply("session-b-left");
        test.Chat().Reply("session-b-right");

        var orchestration = test.Client.Get<IConcurrentProbe>("session-team");
        var first = await orchestration.Respond(
            [new ChatMessage(ChatRole.User, "turn one")]);
        var second = await orchestration.Respond(
            [new ChatMessage(ChatRole.User, "turn two")]);

        Assert.False(string.IsNullOrWhiteSpace(first.Text));
        Assert.False(string.IsNullOrWhiteSpace(second.Text));
        Assert.Equal(4, test.Chat().CallCount);
    }

    [Fact(DisplayName =
        "Concurrent.Respond after participant change on the same id demands migration or reset")]
    public async Task ParticipantChangeOnSameIdDemandsMigrationOrReset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("fingerprint-left");
        test.Chat().Reply("fingerprint-right");

        var orchestration = test.Client.Get<IParticipantSwapConcurrentProbe>("fingerprint-team");
        var first = await orchestration.Respond(
            [new ChatMessage(ChatRole.User, "turn one")]);
        Assert.False(string.IsNullOrWhiteSpace(first.Text));
        Assert.Equal(2, test.Chat().CallCount);

        await orchestration.UseParticipants("left-alt", "right-alt");

        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestration.Respond(
                [new ChatMessage(ChatRole.User, "turn two")]));

        Assert.Contains(
            "incompatible with the current orchestration definition",
            mismatch.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "migration or reset",
            mismatch.Message,
            StringComparison.Ordinal);
        Assert.Equal(2, test.Chat().CallCount);
    }
}
