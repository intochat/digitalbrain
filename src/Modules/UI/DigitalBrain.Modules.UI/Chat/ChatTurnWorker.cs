using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using DigitalBrain.Execution;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

// One worker instance per chat (instance name = chat name). Runs the AI attempt
// for a durable turn without the HTTP observer's cancellation token.
// Does NOT implement IWorker — that contract maps 1:1 to a grain type (harness uses "worker").
[GrainType(GrainTypeName)]
internal sealed class ChatTurnWorker :
    Neuron,
    IHandle<DispatchWorkerAccept>,
    IHandle<DispatchWorkerContinue>,
    IHandle<DispatchWorkerCancel>
{
    internal const string GrainTypeName = "chat-turn-worker";

    private CancellationTokenSource? _attemptCts;
    private AttemptRequest? _active;

    public static NeuronId ForChat(NeuronId chat)
        => new(GrainTypeName, chat.Owner, chat.Name);

    public Task HandleAsync(DispatchWorkerAccept envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        return Accept(envelope.Request);
    }

    public Task HandleAsync(DispatchWorkerContinue envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task HandleAsync(DispatchWorkerCancel envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        return Cancel(envelope.Cursor);
    }

    private async Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Goal is not ChatTurnGoal goal)
        {
            throw new NeuronAuthorizationException(
                $"Chat turn worker '{Id}' refuses goal type '{request.Goal?.GetType().Name}'.");
        }

        _active = request;
        _attemptCts?.Dispose();
        _attemptCts = new CancellationTokenSource();
        var attemptToken = _attemptCts.Token;

        await SendAsync(
            request.Execution,
            new AttemptAccepted(request.Execution, request.Worker, request.Attempt, request.Revision))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        try
        {
            var (answer, author) = await RunResponderAsync(goal, attemptToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (string.IsNullOrWhiteSpace(answer))
            {
                await FinishAsync(
                    request,
                    goal,
                    ChatTurnStatus.Completed,
                    text: null,
                    author: null,
                    detail: "empty-answer",
                    succeeded: true).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            await FinishAsync(
                request,
                goal,
                ChatTurnStatus.Completed,
                answer,
                author,
                detail: null,
                succeeded: true).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException) when (attemptToken.IsCancellationRequested)
        {
            await FinishAsync(
                request,
                goal,
                ChatTurnStatus.Cancelled,
                text: null,
                author: null,
                detail: "cancelled",
                succeeded: false,
                cancelled: true).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            await FinishAsync(
                request,
                goal,
                ChatTurnStatus.Failed,
                text: null,
                author: null,
                detail: failure.Message,
                succeeded: false).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private async Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        _attemptCts?.Cancel();

        if (_active is null || _active.Attempt != cursor.Attempt)
        {
            await SendAsync(
                cursor.Execution,
                new AttemptCancelled(cursor.Execution, cursor.Worker, cursor.Attempt, cursor.Revision))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    private async Task<(string Answer, string Author)> RunResponderAsync(
        ChatTurnGoal goal,
        CancellationToken cancellationToken)
    {
        var chat = GrainFactory.GetGrain<IChat>(goal.Chat.ToGrainId());
        var transcript = await chat.Read()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var (responder, author) = await ResponderAsync(goal.Chat)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var conversationContext = new ChatMessage(
            ChatRole.System,
            $"This conversation lives in neuron {goal.Chat}. Route cards and notes into it by "
            + $"targeting 'chat:{goal.Chat.Name}' or wiring connections whose target is {goal.Chat}.");

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

        return (answer.ToString(), author);
    }

    private async Task<(IAgent Responder, string Author)> ResponderAsync(NeuronId chatId)
    {
        using var lookup = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            var routes = await GrainFactory
                .GetGrain<ISynapseGraph>(ISynapseGraph.ForOwner(chatId.Owner).ToGrainId())
                .ConnectionsFrom(chatId, ChatRoles.Responder)
                .WaitAsync(lookup.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (routes.FirstOrDefault() is { } bound)
            {
                return (
                    GrainFactory.GetGrain<IAgent>(bound.Target.ToGrainId()),
                    bound.Target.Name);
            }

            return (DefaultResponder(chatId.Owner), "assistant");
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested)
        {
            return (DefaultResponder(chatId.Owner), "assistant");
        }
    }

    private IAssistant DefaultResponder(OwnerId owner)
        => GrainFactory.GetGrain<IAssistant>(NeuronId.For<IAssistant>(owner, "assistant").ToGrainId());

    private async Task FinishAsync(
        AttemptRequest request,
        ChatTurnGoal goal,
        ChatTurnStatus status,
        string? text,
        string? author,
        string? detail,
        bool succeeded,
        bool cancelled = false)
    {
        await SendAsync(
            goal.Chat,
            new CompleteTurnWork(goal.TurnId, goal.CommandId, status, text, author, detail))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (cancelled)
        {
            await SendAsync(
                request.Execution,
                new AttemptCancelled(request.Execution, request.Worker, request.Attempt, request.Revision))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        if (succeeded)
        {
            await SendAsync(
                request.Execution,
                new AttemptSucceeded(
                    request.Execution,
                    request.Worker,
                    request.Attempt,
                    request.Revision,
                    new ChatTurnResult(text ?? string.Empty, author ?? string.Empty),
                    Evidence: []))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
            return;
        }

        await SendAsync(
            request.Execution,
            new AttemptFailed(
                request.Execution,
                request.Worker,
                request.Attempt,
                request.Revision,
                new ChatTurnFailure(detail ?? "turn failed"),
                Retryable: false))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);
}
