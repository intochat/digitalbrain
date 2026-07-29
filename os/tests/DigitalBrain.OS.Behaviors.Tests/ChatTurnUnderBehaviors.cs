using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.OS.Behaviors.Tests;

public sealed class ChatTurnUnderBehaviors(OSBehaviorsFixture fixture)
{
    private const int FactTimeout = 120_000;
    private const string ChatName = "streaming-under-behaviors";
    private const string NonStreamedChatName = "sent-under-behaviors";
    private const string SentCorrelationChatName = "sent-correlation";
    private const string StreamedCorrelationChatName = "streamed-correlation";
    private const string Prompt = "who else is listening to this conversation?";
    private const string OwnedAnswer = "Only the conversation itself.";
    private const string CompetingAnswer = "A second answerer reached the same conversation.";
    private const int SettleAttempts = 20;

    private static readonly Assembly ChatVocabulary = typeof(UserMessaged).Assembly;
    private static readonly TimeSpan SettleStep = TimeSpan.FromMilliseconds(100);

    [Fact(Timeout = FactTimeout, DisplayName =
        "a streamed chat turn journals exactly two chat facts where OS.Behaviors is loaded")]
    public async Task StreamedTurnJournalsExactlyTwoChatFactsWhereBehavioursAreLoaded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(OwnedAnswer);
        test.Chat().Reply(CompetingAnswer);

        var chat = test.Neuron<IChat>(ChatName);
        var chunks = new List<string>();

        await foreach (var chunk in chat.Reference
            .SendStreaming(new SendMessage(CommandId.New(), Prompt), cancellationToken))
        {
            chunks.Add(chunk.Text);
        }

        var answered = string.Concat(chunks);

        for (var settle = 0; settle < SettleAttempts; settle++)
        {
            var journaled = await chat.Outgoing.ReadAsync<Synapse>(
                afterSequence: 0, cancellationToken: cancellationToken);

            Assert.Collection(
                journaled.Where(fact => fact.Synapse.GetType().Assembly == ChatVocabulary),
                fact => Assert.Equal(Prompt, Assert.IsType<UserMessaged>(fact.Synapse).Text),
                fact => Assert.Equal(answered, Assert.IsType<AssistantResponded>(fact.Synapse).Text));

            Assert.Equal(
                [new ChatTurn(FromUser: true, Prompt), new ChatTurn(FromUser: false, answered)],
                (await chat.Reference.Read()).Turns);

            await Task.Delay(SettleStep, cancellationToken);
        }
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "a non-streamed chat turn journals both facts under the caller's own command id")]
    public async Task NonStreamedTurnJournalsBothFactsUnderTheCallersCommandId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(OwnedAnswer);
        test.Chat().Reply(CompetingAnswer);

        var chat = test.Neuron<IChat>(NonStreamedChatName);
        var command = CommandId.New();

        await test.Client.Get<IChat>(NonStreamedChatName).Send(new SendMessage(command, Prompt));

        for (var settle = 0; settle < SettleAttempts; settle++)
        {
            var journaled = await chat.Outgoing.ReadAsync<Synapse>(
                afterSequence: 0, cancellationToken: cancellationToken);

            Assert.Collection(
                journaled.Where(fact => fact.Synapse.GetType().Assembly == ChatVocabulary),
                fact =>
                {
                    var messaged = Assert.IsType<UserMessaged>(fact.Synapse);
                    Assert.Equal(Prompt, messaged.Text);
                    Assert.Equal(command, messaged.CommandId);
                },
                fact =>
                {
                    var answered = Assert.IsType<AssistantResponded>(fact.Synapse);
                    Assert.Equal(OwnedAnswer, answered.Text);
                    Assert.Equal(command, answered.CommandId);
                });

            await Task.Delay(SettleStep, cancellationToken);
        }
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "IChat.Send journals one turn under one correlation id")]
    public async Task SentTurnJournalsOneCorrelationId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(OwnedAnswer);

        var chat = test.Neuron<IChat>(SentCorrelationChatName);

        await test.Client.Get<IChat>(SentCorrelationChatName).Send(new SendMessage(CommandId.New(), Prompt));

        AssertOneCorrelation(await ChatFactsAsync(chat, cancellationToken));
    }

    [Fact(Timeout = FactTimeout, DisplayName =
        "IChat.SendStreaming journals one turn under one correlation id")]
    public async Task StreamedTurnJournalsOneCorrelationId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(OwnedAnswer);

        var chat = test.Neuron<IChat>(StreamedCorrelationChatName);

        await foreach (var _ in chat.Reference
            .SendStreaming(new SendMessage(CommandId.New(), Prompt), cancellationToken))
        {
        }

        AssertOneCorrelation(await ChatFactsAsync(chat, cancellationToken));
    }

    private static async Task<ObservedSynapse<Synapse>[]> ChatFactsAsync(
        TestNeuron<IChat> chat,
        CancellationToken cancellationToken)
        => [.. (await chat.Outgoing.ReadAsync<Synapse>(afterSequence: 0, cancellationToken: cancellationToken))
            .Where(fact => fact.Synapse.GetType().Assembly == ChatVocabulary)];

    private static void AssertOneCorrelation(ObservedSynapse<Synapse>[] facts)
    {
        Assert.Collection(
            facts,
            fact => Assert.IsType<UserMessaged>(fact.Synapse),
            fact => Assert.IsType<AssistantResponded>(fact.Synapse));
        Assert.Equal(facts[0].CorrelationId, facts[1].CorrelationId);
    }
}
