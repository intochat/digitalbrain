using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.UI;

[GrainType("chat")]
internal sealed class Chat : Neuron, IChat
{
    private const string AssistantName = "assistant";
    private const string CommandLogName = "chat.command-log";
    private const string TranscriptName = "chat.transcript";
    private const int RememberedCommands = 64;
    private const int RetainedTurns = 64;
    private const string ShowTimeAction = "show-time";
    private const string ShowTimeButtonId = "show-time";

    private readonly IDurableList<byte[]> _commandLog;
    private readonly IDurableList<byte[]> _transcript;
    private readonly Serializer<OwnerCommand> _commands;
    private readonly Serializer<ChatTurn> _turns;

    public Chat()
    {
        _commandLog = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(CommandLogName);
        _transcript = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(TranscriptName);
        _commands = ServiceProvider.GetRequiredService<Serializer<OwnerCommand>>();
        _turns = ServiceProvider.GetRequiredService<Serializer<ChatTurn>>();
    }

    public async Task Send(SendMessage message)
    {
        await foreach (var _ in SendStreaming(message, CancellationToken.None).ConfigureAwait(true))
        {
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IsUnseenCommand(message))
        {
            yield break;
        }

        await RememberOwnerTurnAsync(message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (WantsTimeButton(message.Text))
        {
            const string reply =
                "Tap the button to show the current UTC time.";
            var buttons = new[]
            {
                new ChatButtonOffer(ShowTimeButtonId, "Show current time", ShowTimeAction),
            };
            await ArmOfferedButtonAsync(message.CommandId, ShowTimeButtonId)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            Remember(new ChatTurn(FromUser: false, reply, buttons));
            await EmitAsync(new Responded(message.CommandId, Id, reply, buttons))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            yield return new ChatResponseUpdate(ChatRole.Assistant, reply);
            yield break;
        }

        var answer = new StringBuilder();
        var responder = await ResponderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await foreach (var chunk in responder.RespondStreaming([.. Turns().Select(AsChatMessage)], cancellationToken).ConfigureAwait(true))
        {
            answer.Append(chunk.Text);
            yield return chunk;
        }

        var answered = answer.ToString();
        if (string.IsNullOrWhiteSpace(answered))
        {
            yield break;
        }

        Remember(new ChatTurn(FromUser: false, answered));
        await EmitAsync(new Responded(message.CommandId, Id, answered))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task<ChatTranscript> Read() => Task.FromResult(new ChatTranscript(Turns()));

    public async Task HandleAsync(ReadTranscriptRequest synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);

        var subject = NeuronId.For<IChat>(Id.Owner, synapse.ChatName);
        var transcript = subject == Id
            ? new ChatTranscript(Turns())
            : await GrainFactory.GetGrain<IChat>(subject.ToGrainId()).Read().WaitAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await ReplyAsync(
            new TranscriptRead(synapse.CommandId, subject, Trimmed(transcript, synapse.MaxTurns)),
            cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(ShowTime synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        var when = TimeProvider.GetUtcNow().ToString("O");
        var text = $"Current UTC time: {when}";
        Remember(new ChatTurn(FromUser: false, text));
        await EmitAsync(new Responded(CommandId.New(), Id, text))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private async Task ArmOfferedButtonAsync(CommandId offer, string buttonId)
    {
        var button = NeuronId.For<IButton>(Id.Owner, ChatButtons.OfferedInstanceName(Id.Name, offer, buttonId));
        var graphId = ISynapseGraph.ForOwner(Id.Owner);

        await SendAsync(
            graphId,
            new Connect(
                ChatButtons.ArmingConnectionId(button),
                button,
                ButtonActivated.AliasName,
                Id,
                ButtonActivatedToShowTime.TransformName)).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        // The offer must not reach the owner before the click route is live, or an
        // immediate click emits an activation with no receiver and is lost.
        using var arming = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        await FlushOutboxAsync(arming.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var routes = await GrainFactory
            .GetGrain<ISynapseGraph>(graphId.ToGrainId())
            .ConnectionsFrom(button, ButtonActivated.AliasName)
            .WaitAsync(arming.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (routes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Chat '{Id}' could not arm button '{button}' before offering it.");
        }
    }

    private static ChatTranscript Trimmed(ChatTranscript transcript, int? maxTurns)
        => maxTurns is not { } cap || transcript.Turns.Count <= cap
            ? transcript
            : new ChatTranscript([.. transcript.Turns.Skip(transcript.Turns.Count - cap)]);

    private static bool WantsTimeButton(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.AsSpan().Trim();
        return value.Contains("button", StringComparison.OrdinalIgnoreCase)
            && value.Contains("time", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<ChatTurn> Turns() => [.. _transcript.Select(_turns.Deserialize)];

    private async Task<IAgent> ResponderAsync()
    {
        using var lookup = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            var routes = await GrainFactory
                .GetGrain<ISynapseGraph>(ISynapseGraph.ForOwner(Id.Owner).ToGrainId())
                .ConnectionsFrom(Id, ChatRoles.Responder)
                .WaitAsync(lookup.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            return routes.FirstOrDefault() is { } bound
                ? GrainFactory.GetGrain<IAgent>(bound.Target.ToGrainId())
                : DefaultResponder();
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested)
        {
            return DefaultResponder();
        }
    }

    private IAssistant DefaultResponder()
        => GrainFactory.GetGrain<IAssistant>(NeuronId.For<IAssistant>(Id.Owner, AssistantName).ToGrainId());

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);

    private bool IsUnseenCommand(SendMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Text);
        if (message.CommandId.Value == Guid.Empty)
        {
            throw new ArgumentException("The command id cannot be empty.", nameof(message));
        }

        for (var remembered = _commandLog.Count - 1; remembered >= 0; remembered--)
        {
            var command = _commands.Deserialize(_commandLog[remembered]);
            if (command.CommandId != message.CommandId.Value)
            {
                continue;
            }

            if (!string.Equals(command.Text, message.Text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A chat command id cannot be reused with different text.");
            }

            return false;
        }

        return true;
    }

    private Task RememberOwnerTurnAsync(SendMessage message)
    {
        Remember(message.CommandId, message.Text);
        Remember(new ChatTurn(FromUser: true, message.Text));
        return EmitAsync(new UserMessaged(message.CommandId, Id, message.Text));
    }

    private void Remember(CommandId commandId, string text)
        => Append(
            _commandLog,
            _commands.SerializeToArray(new OwnerCommand(commandId.Value, text)),
            RememberedCommands);

    private void Remember(ChatTurn turn)
        => Append(_transcript, _turns.SerializeToArray(turn), RetainedTurns);

    private static void Append(IDurableList<byte[]> entries, byte[] entry, int retained)
    {
        entries.Add(entry);
        while (entries.Count > retained)
        {
            entries.RemoveAt(0);
        }
    }

    [GenerateSerializer]
    internal sealed record OwnerCommand([property: Id(0)] Guid CommandId, [property: Id(1)] string Text);
}
