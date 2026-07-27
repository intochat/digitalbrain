using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class OrchestrationL1(ModuleFixture fixture)
{
    private const string Left = "left-reply";
    private const string Right = "right-reply";
    private const string Prompt = "prompt";
    private const string SupervisedTeam = "supervised-team";
    private const int Pair = 2;

    [Fact(DisplayName = "Concurrent.Respond fans out to multiple scripted participants")]
    public Task ConcurrentRespondFansOutToMultipleParticipants()
        => FanOutAsync(test => test.Client.Get<IConcurrentProbe>("concurrent-team"));

    [Fact(DisplayName = "GroupChat.Respond runs multiple scripted participants")]
    public Task GroupChatRespondRunsMultipleParticipants()
        => FanOutAsync(test => test.Client.Get<IGroupChatProbe>("group-team"));

    [Fact(DisplayName = "GroupChat supervised Accept/Continue/Cancel throw until the Orleans path is built")]
    public async Task GroupChatSupervisedAttemptsThrow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var worker = test.Client.Get<IGroupChatProbe>(SupervisedTeam);
        var workerId = test.Neuron<IGroupChatProbe>(SupervisedTeam).Id;
        var taskId = test.Neuron<ILlama32>("task-placeholder").Id;
        var attempt = new AttemptId(Guid.NewGuid());
        var request = new AttemptRequest(
            taskId,
            workerId,
            attempt,
            Revision: 0,
            new ProbeGoal("supervised"));
        var cursor = new AttemptCursor(taskId, workerId, attempt, Revision: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.InvokeAccept(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.InvokeContinue(cursor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.InvokeCancel(cursor));
    }

    [Fact(DisplayName = "second Concurrent.Respond reuses the durable session on the same orchestration id")]
    public async Task SecondConcurrentRespondReusesDurableSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);
        ScriptPair(test);

        var orchestration = test.Client.Get<IConcurrentProbe>("session-team");
        var first = await orchestration.Respond([User()]);
        var second = await orchestration.Respond([User()]);

        Assert.False(string.IsNullOrWhiteSpace(first.Text));
        Assert.False(string.IsNullOrWhiteSpace(second.Text));
        Assert.Equal(Pair * 2, test.Chat().CallCount);
    }

    [Fact(DisplayName =
        "Concurrent.Respond after participant change on the same id demands migration or reset")]
    public async Task ParticipantChangeOnSameIdDemandsMigrationOrReset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);

        var orchestration = test.Client.Get<IParticipantSwapConcurrentProbe>("fingerprint-team");
        var first = await orchestration.Respond([User()]);
        AssertEither(first);
        Assert.Equal(Pair, test.Chat().CallCount);

        await orchestration.UseParticipants("left-alt", "right-alt");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestration.Respond([User()]));

        Assert.Equal(Pair, test.Chat().CallCount);
    }

    private async Task FanOutAsync(Func<TestBrain, IAgent> resolve)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ScriptPair(test);

        var response = await resolve(test).Respond([User()]);
        AssertEither(response);
        Assert.Equal(Pair, test.Chat().CallCount);
    }

    private static ChatMessage User() => new(ChatRole.User, Prompt);

    private static void ScriptPair(TestBrain test)
    {
        test.Chat().Reply(Left);
        test.Chat().Reply(Right);
    }

    private static void AssertEither(ChatResponse response)
        => Assert.True(
            response.Text.Contains(Left, StringComparison.Ordinal)
            || response.Text.Contains(Right, StringComparison.Ordinal),
            $"Expected '{Left}' or '{Right}' in '{response.Text}'.");
}
