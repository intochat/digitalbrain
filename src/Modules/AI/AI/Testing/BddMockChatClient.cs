using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Testing-mode stand-in for every LLM. It never fakes tool EFFECTS: each scripted When step
// becomes a FunctionCallContent that FunctionInvokingChatClient executes against the real
// SystemTools 'fire' pipeline, and the loop calls back here with the results appended.
internal sealed partial class BddMockChatClient(BddScenarioCorpus corpus) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ChatResponse(NextMessage([.. messages])));
    }

    // Agent.RespondStreaming accumulates updates (ToChatResponse semantics), so one update
    // carrying the whole scripted message is a faithful stream.
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return StreamAsync(NextMessage([.. messages]), cancellationToken);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        yield return new ChatResponseUpdate(message.Role, message.Contents)
        {
            FinishReason = message.Contents.Any(static content => content is FunctionCallContent)
                ? ChatFinishReason.ToolCalls
                : ChatFinishReason.Stop,
        };
    }

    private ChatMessage NextMessage(IReadOnlyList<ChatMessage> messages)
    {
        var prompt = messages.LastOrDefault(static message => message.Role == ChatRole.User)?.Text
            ?? throw MockLlmMissException.ForPrompt("(no user message)", corpus.GivenPatterns);
        var scenario = corpus.Match(prompt)
            ?? throw MockLlmMissException.ForPrompt(prompt, corpus.GivenPatterns);

        // Script position is derived, never stored: FunctionInvokingChatClient appends each
        // tool result to the message list before calling back, so the FunctionResultContent
        // count IS the number of scripted fires already executed this turn. Retries and
        // replays land on the right step with no mutable state here.
        var completedFires = messages.Sum(static message =>
            message.Contents.Count(static content => content is FunctionResultContent));
        if (completedFires >= scenario.Fires.Count)
        {
            return new ChatMessage(ChatRole.Assistant, scenario.FinalReply);
        }

        var fire = scenario.Fires[completedFires];
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contract"] = fire.Contract,
            ["arguments"] = fire.Arguments,
        };
        if ((fire.TargetsChat ? ChatNeuronTargetIn(messages) : fire.Target) is { } target)
        {
            arguments["target"] = target;
        }

        return new ChatMessage(
            ChatRole.Assistant,
            [new FunctionCallContent($"bdd-fire-{completedFires}", SystemTools.Fire, arguments)]);
    }

    // ChatTurnWorker prepends the turn's system message as:
    //   "This conversation lives in neuron {chat}. Route cards and notes into it by
    //    targeting 'chat:{chat.Name}' or wiring connections whose target is {chat}."
    // The corpus clause 'at the chat' resolves to that quoted chat:{name} target.
    private static string ChatNeuronTargetIn(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role != ChatRole.System)
            {
                continue;
            }

            var shape = ChatNeuronTargetShape().Match(message.Text);
            if (shape.Success)
            {
                return shape.Groups["target"].Value;
            }
        }

        throw new InvalidOperationException(
            "A corpus step fires 'at the chat', but no system message announces the chat "
            + "neuron (\"targeting 'chat:<name>'\"), so there is no chat target to resolve.");
    }

    [GeneratedRegex("targeting '(?<target>chat:[^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex ChatNeuronTargetShape();
}
