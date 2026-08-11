using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Modules.Sdk.Mcp;
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

        var answer = new StringBuilder();
        var (responder, author) = await ResponderAsync().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var conversationContext = new ChatMessage(
            ChatRole.System,
            $"This conversation lives in neuron {Id}. Route cards and notes into it by "
            + $"targeting 'chat:{Id.Name}' or wiring connections whose target is {Id}.");

        // Authenticated Actor on SendMessage is the verified principal for this turn.
        // Assistant fire() stamps it onto actor-bearing synapses — model-supplied Actor dies.
        using (VerifiedActor.Enter(message.Actor))
        {
            await foreach (var chunk in responder.RespondStreaming(
                [conversationContext, .. Turns().Select(AsChatMessage)], cancellationToken).ConfigureAwait(true))
            {
                answer.Append(chunk.Text);
                yield return chunk;
            }
        }

        var answered = answer.ToString();
        if (string.IsNullOrWhiteSpace(answered))
        {
            yield break;
        }

        Remember(new ChatTurn(FromUser: false, answered));
        await EmitAsync(new Responded(message.CommandId, Id, answered, Author: author))
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

    public async Task HandleAsync(Note synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Text))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses an empty note.");
        }

        Remember(new ChatTurn(FromUser: false, synapse.Text));
        await EmitAsync(new Responded(CommandId.New(), Id, synapse.Text, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public async Task HandleAsync(TimerCard synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.Label))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses a timer card without a label.");
        }

        ChatTimerOffer[] offers = [new ChatTimerOffer(synapse.Label, synapse.DueAt)];
        Remember(new ChatTurn(FromUser: false, synapse.Label, Timers: offers));
        await EmitAsync(new Responded(CommandId.New(), Id, synapse.Label, Timers: offers, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    // Directed AuthorizationRequired only (not IHandle — that would broadcast-ghost
    // every chat). Opens a transcript button whose action is the sign-in URL.
    protected override async Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        if (synapse is not AuthorizationRequired required)
        {
            await base.OnUnboundSynapseAsync(synapse, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(required.ServerDisplayName))
        {
            throw new NeuronAuthorizationException($"Chat '{Id}' refuses a sign-in offer without a server name.");
        }

        var label = $"Sign in via {required.ServerDisplayName}";
        var buttonId = $"sign-in-{required.ServerKey}";
        var action = required.SignInUrl.AbsoluteUri;
        ChatButtonOffer[] buttons = [new ChatButtonOffer(buttonId, label, action)];
        var text = $"{required.ServerDisplayName} needs sign-in before that request can continue.";
        Remember(new ChatTurn(FromUser: false, text, buttons));
        await EmitAsync(new Responded(required.CommandId, Id, text, buttons, Author: Id.Name))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static ChatTranscript Trimmed(ChatTranscript transcript, int? maxTurns)
        => maxTurns is not { } cap || transcript.Turns.Count <= cap
            ? transcript
            : new ChatTranscript([.. transcript.Turns.Skip(transcript.Turns.Count - cap)]);

    private IReadOnlyList<ChatTurn> Turns() => [.. _transcript.Select(_turns.Deserialize)];

    private async Task<(IAgent Responder, string Author)> ResponderAsync()
    {
        using var lookup = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            var routes = await GrainFactory
                .GetGrain<ISynapseGraph>(ISynapseGraph.ForOwner(Id.Owner).ToGrainId())
                .ConnectionsFrom(Id, ChatRoles.Responder)
                .WaitAsync(lookup.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (routes.FirstOrDefault() is { } bound)
            {
                return (
                    GrainFactory.GetGrain<IAgent>(bound.Target.ToGrainId()),
                    bound.Target.Name);
            }

            return (DefaultResponder(), AssistantName);
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested)
        {
            return (DefaultResponder(), AssistantName);
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
        Remember(message.CommandId, message.Text, message.Actor);
        Remember(new ChatTurn(FromUser: true, message.Text));
        return EmitAsync(new UserMessaged(message.CommandId, Id, message.Text, message.Actor));
    }

    private void Remember(CommandId commandId, string text, ActorContext? actor)
        => Append(
            _commandLog,
            _commands.SerializeToArray(new OwnerCommand(commandId.Value, text, actor)),
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
    internal sealed record OwnerCommand(
        [property: Id(0)] Guid CommandId,
        [property: Id(1)] string Text,
        [property: Id(2)] ActorContext? Actor = null);
}
