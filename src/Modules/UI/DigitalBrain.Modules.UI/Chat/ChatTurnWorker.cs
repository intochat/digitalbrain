using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

// One worker instance per chat (instance name = chat name). Runs the AI attempt for a
// durable turn off the chat's own activation, so Chat stays free to serve reads and card
// deliveries while the call is in flight and without the HTTP observer's cancellation token.
[GrainType(GrainTypeName)]
internal sealed class ChatTurnWorker : Neuron, IChatTurnWorker
{
    internal const string GrainTypeName = "chat-turn-worker";

    public static NeuronId ForChat(NeuronId chat)
        => new(GrainTypeName, chat.Owner, chat.Name);

    public async Task<ChatTurnResult> RunAsync(ChatTurnGoal goal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(goal);
        cancellationToken.ThrowIfCancellationRequested();

        var executionId = await StartTurnExecutionAsync(goal, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var (answer, author) = await RunResponderAsync(goal, executionId, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return new ChatTurnResult(answer, author);
    }

    private async Task<ExecutionId> StartTurnExecutionAsync(ChatTurnGoal goal, CancellationToken cancellationToken)
    {
        var chat = GrainFactory.GetGrain<IChat>(goal.Chat.ToGrainId());
        var prior = await chat.ReadActiveExecution()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var executionId = ExecutionId.New();
        IReadOnlyList<ExecutionId>? related = prior is { } active ? [active] : null;

        var execution = GrainFactory.GetGrain<IExecution>(
            NeuronId.For<IExecution>(goal.Chat.Owner, executionId.ToString()).ToGrainId());

        // Empty grants: chat Agent path must not fan-out Capabilities. Tools call ExecutionSession later.
        await execution.HandleAsync(
                new StartExecution(
                    CommandId.New(),
                    executionId,
                    new ChatTurnWorkload(goal.Chat, goal.TurnId, goal.Text),
                    ExecutionDriverKind.Agent,
                    Grants: [],
                    related),
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        await chat.SetActiveExecution(executionId)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        return executionId;
    }

    private async Task<(string Answer, string Author)> RunResponderAsync(
        ChatTurnGoal goal,
        ExecutionId executionId,
        CancellationToken cancellationToken)
    {
        var chat = GrainFactory.GetGrain<IChat>(goal.Chat.ToGrainId());
        var transcript = await chat.Read()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var execution = GrainFactory.GetGrain<IExecution>(
            NeuronId.For<IExecution>(goal.Chat.Owner, executionId.ToString()).ToGrainId());
        var projection = await execution.Read()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var context = GrainFactory.GetGrain<IExecutionContext>(
            EntityId.For<IExecutionContext>(goal.Chat.Owner, executionId.ToString()).ToGrainId());
        var contextState = await context.Read()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var responder = DefaultResponder(goal.Chat.Owner);

        // The quoted value here is the chat's FULL grain key ("{owner}/{principal:N}.{local}"),
        // not just its local Name: KitToolSource's render_chart/generate_image tools take
        // chatName straight into IGrainFactory.GetGrain<IChat>(string) and KitInstanceNames.Sibling,
        // both of which require the owner-qualified key (verified in Task 7/9 -- goal.Chat.Name
        // alone 404s every kit lookup the tool call would make).
        var system = new StringBuilder()
            .Append("This conversation lives in chat '").Append(goal.Chat.GrainKey)
            .Append("'. Send cards and notes by targeting 'chat:").Append(goal.Chat.Name).Append("'.")
            .Append(" Active execution: ").Append(executionId).Append('.');

        if (projection.PromptBlocks is { Count: > 0 } blocks)
        {
            system.Append(" Provider context: ").Append(string.Join(" | ", blocks));
        }

        if (contextState?.Slots is { Count: > 0 } slots)
        {
            system.Append(" ExecutionContext paths: ");
            system.Append(string.Join(", ", slots.Select(slot => slot.Path.Value)));
            foreach (var slot in slots)
            {
                if (!string.IsNullOrWhiteSpace(slot.Entry.PayloadJson))
                {
                    system.Append(" [").Append(slot.Path.Value).Append(": ")
                        .Append(Truncate(slot.Entry.PayloadJson!, 400)).Append(']');
                }
            }
        }

        var conversationContext = new ChatMessage(ChatRole.System, system.ToString());

        var answer = new StringBuilder();
        using (VerifiedActor.Enter(goal.Actor))
        {
            await foreach (var chunk in responder.RespondStreaming(
                [conversationContext, .. transcript.Turns.Select(AsChatMessage)],
                cancellationToken).ConfigureAwait(true))
            {
                answer.Append(chunk.Text);
            }
        }

        return (answer.ToString(), "assistant");
    }

    private IAssistant DefaultResponder(OwnerId owner)
        => GrainFactory.GetGrain<IAssistant>(NeuronId.For<IAssistant>(owner, "assistant").ToGrainId());

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);

    private static string Truncate(string value, int maxChars)
        => value.Length <= maxChars ? value : value[..maxChars] + "…";
}
