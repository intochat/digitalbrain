using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

[Alias("DigitalBrain.ModuleTests.IGatedParticipantProbe")]
public partial interface IGatedParticipantProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.IStreamingConcurrentProbe")]
public partial interface IStreamingConcurrentProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.IStreamingGroupChatProbe")]
public partial interface IStreamingGroupChatProbe : IGroupChat;

[Alias("DigitalBrain.ModuleTests.IHeldFirstGroupChatProbe")]
public partial interface IHeldFirstGroupChatProbe : IGroupChat;

public sealed class GatedParticipantProbe : Neuron, IGatedParticipantProbe
{
    internal const string HeldName = "gated-held";
    internal const string ImmediateName = "gated-immediate";
    internal const string HeldReply = "held-participant-reply";
    internal const string ImmediateReply = "immediate-participant-reply";

    private static readonly TimeSpan HoldBudget = TimeSpan.FromSeconds(60);

    private static TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource _heldParticipantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _immediateParticipantCalls;

    internal static Task HeldParticipantEntered => _heldParticipantEntered.Task;

    internal static int ImmediateParticipantCalls => Volatile.Read(ref _immediateParticipantCalls);

    internal static bool Released => _released.Task.IsCompleted;

    internal static void Arm()
    {
        _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _heldParticipantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _immediateParticipantCalls, 0);
    }

    internal static void Release() => _released.TrySetResult();

    public async Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!string.Equals(Id.Name, HeldName, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _immediateParticipantCalls);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ImmediateReply));
        }

        var release = _released.Task;
        _heldParticipantEntered.TrySetResult();
        await release.WaitAsync(HoldBudget);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, HeldReply));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await Respond(messages).WaitAsync(cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }
}

public sealed class StreamingConcurrentProbe : Concurrent, IStreamingConcurrentProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.ImmediateName),
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.HeldName),
    ];
}

public sealed class StreamingGroupChatProbe : GroupChat, IStreamingGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.ImmediateName),
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.HeldName),
    ];
}

public sealed class HeldFirstGroupChatProbe : GroupChat, IHeldFirstGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.HeldName),
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.ImmediateName),
    ];
}

public sealed class OrchestrationStreaming(ModuleFixture fixture)
{
    private const string ConcurrentTeam = "streaming-concurrent-team";
    private const string GroupChatTeam = "streaming-group-chat-team";
    private const string HeldFirstTeam = "held-first-group-chat-team";
    private const string SwapTeam = "streamed-fingerprint-team";
    private const string Prompt = "prompt";
    private const string ScriptedLeft = "scripted-left-reply";
    private const string ScriptedRight = "scripted-right-reply";
    private const int StreamingTimeout = 180_000;

    private static readonly TimeSpan ProgressBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleStep = TimeSpan.FromMilliseconds(100);

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "Concurrent.RespondStreaming yields updates before the orchestration completes")]
    public Task ConcurrentRespondStreamingYieldsBeforeCompletion()
        => YieldsBeforeCompletionAsync(
            test => test.Client.Get<IStreamingConcurrentProbe>(ConcurrentTeam),
            TestContext.Current.CancellationToken);

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "GroupChat.RespondStreaming yields updates before the orchestration completes")]
    public Task GroupChatRespondStreamingYieldsBeforeCompletion()
        => YieldsBeforeCompletionAsync(
            test => test.Client.Get<IStreamingGroupChatProbe>(GroupChatTeam),
            TestContext.Current.CancellationToken);

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "a drained Concurrent.RespondStreaming persists the durable session the next run must match")]
    public async Task DrainedStreamPersistsTheDurableSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedLeft);
        test.Chat().Reply(ScriptedRight);

        var orchestration = test.Client.Get<IParticipantSwapConcurrentProbe>(SwapTeam);

        await foreach (var _ in orchestration.RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellationToken))
        {
        }

        await orchestration.UseParticipants("left-alt", "right-alt");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestration.Respond([new ChatMessage(ChatRole.User, Prompt)]));
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "cancelling GroupChat.RespondStreaming mid-stream stops the orchestration before the next participant runs")]
    public async Task CancellingGroupChatStreamStopsTheOrchestration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GatedParticipantProbe.Arm();

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = test.Client.Get<IHeldFirstGroupChatProbe>(HeldFirstTeam)
            .RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        try
        {
            var drain = DrainAsync(stream);

            await GatedParticipantProbe.HeldParticipantEntered.WaitAsync(ProgressBudget, cancellationToken);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);

            GatedParticipantProbe.Release();

            for (var settle = 0; settle < 20; settle++)
            {
                Assert.Equal(0, GatedParticipantProbe.ImmediateParticipantCalls);

                await Task.Delay(SettleStep, cancellationToken);
            }
        }
        finally
        {
            GatedParticipantProbe.Release();
        }
    }

    private static async Task DrainAsync(IAsyncEnumerator<ChatResponseUpdate> stream)
    {
        while (await stream.MoveNextAsync())
        {
        }
    }

    private async Task YieldsBeforeCompletionAsync(
        Func<TestBrain, IAgent> resolve,
        CancellationToken cancellationToken)
    {
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GatedParticipantProbe.Arm();

        var stream = resolve(test)
            .RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            Assert.True(
                await stream.MoveNextAsync().AsTask().WaitAsync(ProgressBudget, cancellationToken),
                "The stream completed without yielding a single update.");
            Assert.False(
                GatedParticipantProbe.Released,
                "The held participant was released before the first update was observed.");

            var text = new StringBuilder(stream.Current.Text);

            GatedParticipantProbe.Release();

            var updatesAfterRelease = 0;

            while (await stream.MoveNextAsync())
            {
                updatesAfterRelease++;
                text.Append(stream.Current.Text);
            }

            Assert.True(
                updatesAfterRelease > 0,
                "No update arrived after the held participant was released.");
            Assert.Contains(GatedParticipantProbe.HeldReply, text.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            GatedParticipantProbe.Release();
            await stream.DisposeAsync();
        }
    }
}
