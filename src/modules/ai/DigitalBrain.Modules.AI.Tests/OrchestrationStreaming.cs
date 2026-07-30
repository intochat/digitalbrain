using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;
public sealed class OrchestrationStreaming(ModuleFixture fixture)
{
    private const string ConcurrentTeam = "streaming-concurrent-team";
    private const string GroupChatTeam = "streaming-group-chat-team";
    private const string HeldFirstTeam = "held-first-group-chat-team";
    private const string SwapTeam = "streamed-fingerprint-team";
    private const string AbandonTeam = "abandoned-stream-team";
    private const string FragmentedTeam = "fragmented-concurrent-team";
    private const string AttributionTeam = "attributed-concurrent-team";
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
            test => test.Client.GetGrainProxy<IStreamingConcurrentProbe>(ConcurrentTeam),
            TestContext.Current.CancellationToken);

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "GroupChat.RespondStreaming yields updates before the orchestration completes")]
    public Task GroupChatRespondStreamingYieldsBeforeCompletion()
        => YieldsBeforeCompletionAsync(
            test => test.Client.GetGrainProxy<IStreamingGroupChatProbe>(GroupChatTeam),
            TestContext.Current.CancellationToken);

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "a drained Concurrent.RespondStreaming persists the durable session the next run must match")]
    public async Task DrainedStreamPersistsTheDurableSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedLeft);
        test.Chat().Reply(ScriptedRight);

        var orchestration = test.Client.GetGrainProxy<IParticipantSwapConcurrentProbe>(SwapTeam);

        await foreach (var _ in orchestration.RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellationToken))
        {
        }

        await orchestration.UseParticipants("left-alt", "right-alt");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestration.Respond([new ChatMessage(ChatRole.User, Prompt)]));
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "Concurrent.RespondStreaming delivers a participant's first fragment before that participant finishes")]
    public async Task ParticipantFragmentsReachTheCallerBeforeTheParticipantFinishes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        FragmentedParticipantProbe.Arm();

        var stream = test.Client.GetGrainProxy<IFragmentedConcurrentProbe>(FragmentedTeam)
            .RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var text = new StringBuilder();

        try
        {
            await ReadUntilAsync(stream, text, FragmentedParticipantProbe.FirstFragment, cancellationToken);

            Assert.DoesNotContain(FragmentedParticipantProbe.SecondFragment, text.ToString(), StringComparison.Ordinal);

            FragmentedParticipantProbe.Release();

            await ReadUntilAsync(stream, text, FragmentedParticipantProbe.SecondFragment, cancellationToken);
        }
        finally
        {
            FragmentedParticipantProbe.Release();
            await stream.DisposeAsync();
        }
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "a Concurrent participant call is journaled as a capability request attributed to the orchestration")]
    public async Task ParticipantCallIsJournaledAsACapabilityRequestAttributedToTheOrchestration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        FragmentedParticipantProbe.Arm();
        FragmentedParticipantProbe.Release();

        var orchestration = test.Neuron<IFragmentedConcurrentProbe>(AttributionTeam);
        var participant = test.Neuron<IFragmentedParticipantProbe>(FragmentedParticipantProbe.ParticipantName);

        await foreach (var _ in orchestration.Reference
            .RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellationToken))
        {
        }

        var received = await participant.Incoming.ReadAsync<CapabilityRequested>(
            afterSequence: 0, cancellationToken: cancellationToken);
        var participantCall = Assert.Single(
            received,
            fact => fact.Synapse.Method == nameof(IAgent.RespondStreaming));

        Assert.Equal(orchestration.Id, participantCall.Caller);

        var emitted = await orchestration.Outgoing.ReadAsync<CapabilityRequested>(
            afterSequence: 0, cancellationToken: cancellationToken);
        var attributed = Assert.Single(emitted, fact => fact.Synapse.Target == participant.Id);

        Assert.Equal(nameof(IAgent.RespondStreaming), attributed.Synapse.Method);

        var completed = await orchestration.Outgoing.ReadAsync<CapabilityCompleted>(
            afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Single(completed, fact => fact.Synapse.Request == attributed.SynapseId);
        Assert.Empty(await orchestration.Outgoing.ReadAsync<CapabilityFailed>(
            afterSequence: 0, cancellationToken: cancellationToken));
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "an abandoned Concurrent.RespondStreaming deliberately leaves the durable session unwritten")]
    public async Task AbandonedStreamDeliberatelyLeavesTheDurableSessionUnwritten()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedLeft);
        test.Chat().Reply(ScriptedRight);
        test.Chat().Reply(ScriptedLeft);
        test.Chat().Reply(ScriptedRight);

        var orchestration = test.Client.GetGrainProxy<IParticipantSwapConcurrentProbe>(AbandonTeam);
        var stream = orchestration
            .RespondStreaming([new ChatMessage(ChatRole.User, Prompt)], cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        Assert.True(
            await stream.MoveNextAsync().AsTask().WaitAsync(ProgressBudget, cancellationToken),
            "The stream ended before it could be abandoned mid-run.");

        await stream.DisposeAsync();
        await orchestration.UseParticipants("left-alt", "right-alt");

        var afterSwap = await orchestration.Respond([new ChatMessage(ChatRole.User, Prompt)]);

        Assert.False(string.IsNullOrWhiteSpace(afterSwap.Text));
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "cancelling GroupChat.RespondStreaming mid-stream stops the orchestration before the next participant runs")]
    public async Task CancellingGroupChatStreamStopsTheOrchestration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GatedParticipantProbe.Arm();

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = test.Client.GetGrainProxy<IHeldFirstGroupChatProbe>(HeldFirstTeam)
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

    private static async Task ReadUntilAsync(
        IAsyncEnumerator<ChatResponseUpdate> stream,
        StringBuilder text,
        string marker,
        CancellationToken cancellationToken)
    {
        while (!text.ToString().Contains(marker, StringComparison.Ordinal))
        {
            Assert.True(
                await stream.MoveNextAsync().AsTask().WaitAsync(ProgressBudget, cancellationToken),
                $"The stream ended before '{marker}' arrived; it carried '{text}'.");

            text.Append(stream.Current.Text);
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

        var text = new StringBuilder();

        try
        {
            await ReadUntilAsync(stream, text, GatedParticipantProbe.ImmediateReply, cancellationToken);

            Assert.DoesNotContain(GatedParticipantProbe.HeldReply, text.ToString(), StringComparison.Ordinal);

            GatedParticipantProbe.Release();

            await ReadUntilAsync(stream, text, GatedParticipantProbe.HeldReply, cancellationToken);
        }
        finally
        {
            GatedParticipantProbe.Release();
            await stream.DisposeAsync();
        }
    }
}
