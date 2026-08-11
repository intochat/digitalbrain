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
// Directed dispatch only (OnUnbound) — no IHandle<> so DispatchWorker* stay off the broadcast catalog.
// Does NOT implement IWorker: directed unbound dispatch keeps DispatchWorker* off the
// broadcast catalog while the module registers this grain type on the worker allow-list.
[GrainType(GrainTypeName)]
internal sealed class ChatTurnWorker : Neuron
{
    internal const string GrainTypeName = "chat-turn-worker";

    private CancellationTokenSource? _attemptCts;
    private AttemptRequest? _active;
    private int _runGeneration;

    public static NeuronId ForChat(NeuronId chat)
        => new(GrainTypeName, chat.Owner, chat.Name);

    protected override Task OnUnboundSynapseAsync(Synapse synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        return synapse switch
        {
            // Return immediately so DispatchWorkerCancel can interleave (Orleans
            // non-reentrancy would otherwise serialize Cancel behind a long Accept).
            DispatchWorkerAccept accept => BeginAccept(accept.Request),
            DispatchWorkerContinue => Task.CompletedTask,
            DispatchWorkerCancel cancel => Cancel(cancel.Cursor),
            _ => base.OnUnboundSynapseAsync(synapse, cancellationToken),
        };
    }

    private Task BeginAccept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Goal is not ChatTurnGoal)
        {
            throw new NeuronAuthorizationException(
                $"Chat turn worker '{Id}' refuses goal type '{request.Goal?.GetType().Name}'.");
        }

        _active = request;
        _attemptCts?.Dispose();
        _attemptCts = new CancellationTokenSource();
        var attemptToken = _attemptCts.Token;
        var generation = Interlocked.Increment(ref _runGeneration);
        DelayDeactivation(TimeSpan.FromHours(2));
        _ = RunAcceptAsync(request, attemptToken, generation);
        return Task.CompletedTask;
    }

    private async Task RunAcceptAsync(AttemptRequest request, CancellationToken attemptToken, int generation)
    {
        if (request.Goal is not ChatTurnGoal goal)
        {
            return;
        }

        try
        {
            await SendAsync(
                request.Execution,
                new AttemptAccepted(request.Execution, request.Worker, request.Attempt, request.Revision))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            var (answer, author) = await RunResponderAsync(goal, attemptToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (string.IsNullOrWhiteSpace(answer))
            {
                await FinishAsync(
                    request,
                    succeeded: true,
                    cancelled: false,
                    resultText: null,
                    resultAuthor: null,
                    failureDetail: "empty-answer").ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                return;
            }

            await FinishAsync(
                request,
                succeeded: true,
                cancelled: false,
                resultText: answer,
                resultAuthor: author,
                failureDetail: null).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (OperationCanceledException) when (attemptToken.IsCancellationRequested)
        {
            await FinishAsync(
                request,
                succeeded: false,
                cancelled: true,
                resultText: null,
                resultAuthor: null,
                failureDetail: "cancelled").ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        catch (Exception failure)
        {
            await FinishAsync(
                request,
                succeeded: false,
                cancelled: false,
                resultText: null,
                resultAuthor: null,
                failureDetail: failure.Message).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
        finally
        {
            if (generation == _runGeneration && ReferenceEquals(_active, request))
            {
                _active = null;
            }

            DelayDeactivation(TimeSpan.FromMinutes(1));
        }
    }

    private async Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        _attemptCts?.Cancel();

        // Always ack Cancel to the kernel. Waiting for the Accept task to observe the
        // CTS can lose the race to a slow AI stream; AttemptCancelled while Cancelling
        // is the honest terminal (AttemptSucceeded after Cancel is ignored if Cancelled first).
        await SendAsync(
            cursor.Execution,
            new AttemptCancelled(cursor.Execution, cursor.Worker, cursor.Attempt, cursor.Revision))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
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

    // Kernel is truth: only Attempt* facts; Chat reconciles via ExecutionTerminal.
    private async Task FinishAsync(
        AttemptRequest request,
        bool succeeded,
        bool cancelled,
        string? resultText,
        string? resultAuthor,
        string? failureDetail)
    {
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
                    new ChatTurnResult(resultText ?? string.Empty, resultAuthor ?? string.Empty),
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
                new ChatTurnFailure(failureDetail ?? "turn failed"),
                Retryable: false))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);
}
