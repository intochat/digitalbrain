using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.Chat.Tests;

[GrainType("assistant")]
public sealed class ChatStreamingAssistant : Neuron, IAssistant
{
    internal const string Opening = "you said: ";
    internal const string Closing = " -- noted.";

    private static readonly TimeSpan HoldBudget = TimeSpan.FromSeconds(60);

    private static TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Arm() => _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Release() => _released.TrySetResult();

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var release = _released.Task;

        yield return new ChatResponseUpdate(ChatRole.Assistant, Opening);

        await release.WaitAsync(HoldBudget, cancellationToken);

        yield return new ChatResponseUpdate(ChatRole.Assistant, LatestOwnerText(messages));
        yield return new ChatResponseUpdate(ChatRole.Assistant, Closing);
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }

    private static string LatestOwnerText(IReadOnlyList<ChatMessage> messages)
    {
        for (var turn = messages.Count - 1; turn >= 0; turn--)
        {
            if (messages[turn].Role == ChatRole.User)
            {
                return messages[turn].Text;
            }
        }

        return string.Empty;
    }
}

public sealed class ChatStreamingTurn(ChatFixture fixture)
{
    private const string ChatName = "streaming-turn";
    private const string AbandonedChatName = "abandoned-streaming-turn";
    private const string SlowChatName = "slow-answering-turn";
    private const string Prompt = "how does streaming reach the transcript?";
    private const int StreamingTimeout = 180_000;

    private static readonly Assembly ChatVocabulary = typeof(UserMessaged).Assembly;
    private static readonly TimeSpan ProgressBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleStep = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PastResponseTimeout = ChatFixture.ResponseTimeout + TimeSpan.FromSeconds(2);

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "a drained IChat.SendStreaming yields many chunks and journals one AssistantResponded carrying all of them")]
    public async Task DrainedStreamRemembersOneAssistantTurnCarryingEveryChunk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ChatStreamingAssistant.Arm();
        ChatStreamingAssistant.Release();

        var chat = test.Neuron<IChat>(ChatName);
        var chunks = new List<string>();

        await foreach (var chunk in chat.Reference
            .SendStreaming(new SendMessage(CommandId.New(), Prompt), cancellationToken))
        {
            chunks.Add(chunk.Text);
        }

        Assert.True(chunks.Count > 1, $"The stream carried {chunks.Count} chunk(s), so nothing proves it streamed.");

        var answer = string.Concat(chunks);
        var transcript = await chat.Reference.Read();

        Assert.Collection(
            transcript.Turns,
            turn => Assert.Equal(new ChatTurn(FromUser: true, Prompt), turn),
            turn => Assert.Equal(new ChatTurn(FromUser: false, answer), turn));

        var journaled = await chat.Outgoing.ReadAsync<Synapse>(afterSequence: 0, cancellationToken: cancellationToken);

        Assert.Collection(
            journaled.Where(fact => fact.Synapse.GetType().Assembly == ChatVocabulary),
            fact => Assert.Equal(Prompt, Assert.IsType<UserMessaged>(fact.Synapse).Text),
            fact => Assert.Equal(answer, Assert.IsType<AssistantResponded>(fact.Synapse).Text));
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "an abandoned IChat.SendStreaming deliberately leaves the transcript ending at the owner's turn")]
    public async Task AbandonedStreamDeliberatelyRemembersNoAssistantTurn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ChatStreamingAssistant.Arm();

        var chat = test.Neuron<IChat>(AbandonedChatName);
        var stream = chat.Reference
            .SendStreaming(new SendMessage(CommandId.New(), Prompt), cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            Assert.True(
                await stream.MoveNextAsync().AsTask().WaitAsync(ProgressBudget, cancellationToken),
                "The stream ended before it could be abandoned mid-answer.");
            Assert.Equal(ChatStreamingAssistant.Opening, stream.Current.Text);

            await stream.DisposeAsync();

            for (var settle = 0; settle < 20; settle++)
            {
                var transcript = await chat.Reference.Read();

                Assert.Equal([new ChatTurn(FromUser: true, Prompt)], transcript.Turns);
                Assert.Empty(await chat.Outgoing.ReadAsync<AssistantResponded>(
                    afterSequence: 0, cancellationToken: cancellationToken));

                await Task.Delay(SettleStep, cancellationToken);
            }
        }
        finally
        {
            ChatStreamingAssistant.Release();
        }
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "a repeated command id streams nothing again, and the same id with different text is refused")]
    public async Task RepeatedCommandIdIsQuietAndContradictedTextIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ChatStreamingAssistant.Arm();
        ChatStreamingAssistant.Release();

        var chat = test.Client.GetGrainProxy<IChat>("repeated-streaming-turn");
        var command = CommandId.New();

        await DrainAsync(chat.SendStreaming(new SendMessage(command, Prompt), cancellationToken));

        var replayed = new List<string>();

        await foreach (var chunk in chat.SendStreaming(new SendMessage(command, Prompt), cancellationToken))
        {
            replayed.Add(chunk.Text);
        }

        Assert.Empty(replayed);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DrainAsync(chat.SendStreaming(new SendMessage(command, "a different question"), cancellationToken)));

        var transcript = await chat.Read();

        Assert.Equal(2, transcript.Turns.Count);
    }

    [Fact(Timeout = StreamingTimeout, DisplayName =
        "IChat.Send survives a model turn longer than the response timeout")]
    public async Task SendSurvivesAModelTurnLongerThanTheResponseTimeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        ChatStreamingAssistant.Arm();

        var chat = test.Client.GetGrainProxy<IChat>(SlowChatName);
        var sending = chat.Send(new SendMessage(CommandId.New(), Prompt));

        await Task.Delay(PastResponseTimeout, cancellationToken);
        ChatStreamingAssistant.Release();

        await sending;

        var transcript = await chat.Read();

        Assert.Collection(
            transcript.Turns,
            turn => Assert.Equal(new ChatTurn(FromUser: true, Prompt), turn),
            turn => Assert.Equal(
                new ChatTurn(
                    FromUser: false,
                    ChatStreamingAssistant.Opening + Prompt + ChatStreamingAssistant.Closing),
                turn));
    }

    private static async Task DrainAsync(IAsyncEnumerable<ChatResponseUpdate> stream)
    {
        await foreach (var _ in stream)
        {
        }
    }
}
