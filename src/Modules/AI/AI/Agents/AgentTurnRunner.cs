using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain.AI.Conversations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.AI.Agents;

public interface IAgentTurnRunner
{
    IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, CancellationToken ct);
}
public sealed record AgentToolContext(string ScopeId, string RunId, string CallId);
public interface IAgentToolFactory
{
    IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context);
}
public sealed record AgentTurnRequest(string AgentId, string RunId, string ScopeId,
    IReadOnlyList<ConversationTurn> History, string Message, AgentModelSelection? Model,
    string? Instructions = null, IReadOnlyList<string>? ToolNames = null,
    IReadOnlyList<AiMessage>? Messages = null, AiMessage? Input = null,
    bool Streaming = false, int MaxModelCalls = 16, TimeSpan? Timeout = null, InferenceOptions? Options = null);
public abstract record AgentTurnEvent
{
    internal TaskCompletionSource? Observed { get; set; }
    public sealed record Started(string RunId) : AgentTurnEvent;
    public sealed record ModelSelected(ResolvedAgentModel Model, ModelDescriptor Descriptor) : AgentTurnEvent;
    public sealed record Text(string Content) : AgentTurnEvent;
    public sealed record ToolStarted(string CallId, string Name, string Arguments) : AgentTurnEvent;
    public sealed record ToolCompleted(string CallId, string Name, string Result) : AgentTurnEvent;
    public sealed record ToolFailed(string CallId, string Name) : AgentTurnEvent;
    public sealed record Completed(IReadOnlyList<AiMessage> Messages, AgentUsage? Usage) : AgentTurnEvent;
    public sealed record Finished : AgentTurnEvent;
    public sealed record Failed(string Message, bool Cancelled = false) : AgentTurnEvent;
}

// Providers propose calls; this is the single, bounded tool invocation loop.
public sealed class AgentTurnRunner(IServiceProvider services) : IAgentTurnRunner
{
    public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(request.Timeout ?? TimeSpan.FromMinutes(2));
        var events = Channel.CreateBounded<AgentTurnEvent>(new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });
        var execution = Execute(request, events.Writer, lifetime.Token);
        try
        {
            await foreach (var item in events.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return item;
                item.Observed?.TrySetResult();
            }
        }
        finally { await lifetime.CancelAsync().ConfigureAwait(false); await execution.ConfigureAwait(false); }
    }

    private async Task Execute(AgentTurnRequest request, ChannelWriter<AgentTurnEvent> events, CancellationToken ct)
    {
        var sessions = new List<IAgentToolSession>();
        try
        {
            if (request.MaxModelCalls is < 1 or > 128) { throw new ArgumentOutOfRangeException(nameof(request), "MaxModelCalls must be between 1 and 128."); }
            await events.WriteAsync(new AgentTurnEvent.Started(request.RunId), ct).ConfigureAwait(false);
            string? currentCall = null;
            AgentToolContext Context() => new(request.ScopeId, request.RunId, currentCall ?? throw new InvalidOperationException("No active tool call."));
            var selected = request.ToolNames ?? [];
            if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Count) { throw new ArgumentException("Tool names must be unique."); }
            var available = selected.Count == 0 ? [] : services.GetServices<IAgentToolFactory>().SelectMany(f => f.Create(Context)).ToList();
            if (selected.Count > 0 && services.GetService<NativeTools>() is { } native)
            { available.AddRange(native.Resolve(selected).OfType<AIFunction>()); }
            if (selected.Count > 0)
            {
                foreach (var source in services.GetServices<IAgentToolSource>())
                {
                    var session = await source.OpenAsync(selected, Context, ct).ConfigureAwait(false);
                    sessions.Add(session);
                    available.AddRange(session.Tools);
                }
            }
            var tools = selected.Select(name =>
            {
                var matches = available.Where(f => f.Name == name).ToArray();
                return matches.Length == 1 ? matches[0] : throw new InvalidOperationException($"Selected tool '{name}' must have exactly one registration.");
            }).ToArray();

            var configuration = services.GetService<IOptions<AIOptions>>()?.Value;
            var configured = configuration is not null && (configuration.Default.Profile is not null
                || configuration.Default.Model is not null || configuration.Default.Provider is not null
                || configuration.OpenAI.ApiKey is not null || configuration.Anthropic.ApiKey is not null
                || configuration.Google.ApiKey is not null || configuration.XAI.ApiKey is not null || configuration.Ollama.Endpoint is not null);
            IChatClient client;
            bool owned;
            ChatOptions options;
            ModelDescriptor? descriptor = null;
            if (request.Model is not null || configured || request.Options is not null)
            {
                var profiles = services.GetRequiredService<ModelProfiles>();
                var selection = request.Model;
                if (request.Options?.Reasoning is not null || request.Options?.MaxOutputTokens is not null)
                {
                    selection = (selection ?? new()) with
                    {
                        Reasoning = request.Options.Reasoning ?? selection?.Reasoning,
                        MaxOutputTokens = request.Options.MaxOutputTokens ?? selection?.MaxOutputTokens
                    };
                }
                var resolved = profiles.Resolve(selection, requiresTools: tools.Length > 0);
                descriptor = services.GetRequiredService<InferenceService>().DescribeResolved(resolved);
                InferenceMapping.ValidateRequest(new InferenceRequest(
                    [.. request.Messages ?? [], request.Input ?? new AiMessage("user", [new AiText(request.Message)])],
                    selection, request.Options), descriptor);
                options = InferenceMapping.CreateOptions(resolved, request.Options is null ? null : request.Options with { Reasoning = null }, descriptor);
                client = profiles.CreateInferenceClient(resolved);
                owned = true;
                await WriteObserved(events, new AgentTurnEvent.ModelSelected(resolved, descriptor), ct).ConfigureAwait(false);
            }
            else
            {
                client = services.GetRequiredService<IChatClient>();
                options = new();
                owned = false;
            }
            using var ownedClient = owned ? client : null;
            options.Instructions = request.Instructions;
            options.Tools = tools.Cast<AITool>().ToList();
            var messages = request.Messages?.Select(InferenceMapping.ToChatMessage).ToList() ?? [];
            if (request.Messages is null)
            {
                foreach (var turn in request.History)
                {
                    messages.Add(new(ChatRole.User, turn.UserText));
                    messages.Add(new(ChatRole.Assistant, turn.AssistantText + (turn.ResultIds.Count == 0 ? "" : "\nResult windows: " + string.Join(", ", turn.ResultIds))));
                }
            }
            messages.Add(request.Input is null ? new(ChatRole.User, request.Message) : InferenceMapping.ToChatMessage(request.Input));
            var generated = new List<ChatMessage>();
            UsageDetails? usage = null;
            var calls = new HashSet<string>(StringComparer.Ordinal);
            for (var step = 0; step < request.MaxModelCalls; step++)
            {
                ChatResponse response;
                if (request.Streaming)
                {
                    var updates = new List<ChatResponseUpdate>();
                    await foreach (var update in client.GetStreamingResponseAsync(messages, options, ct).ConfigureAwait(false))
                    {
                        updates.Add(update);
                        if (!string.IsNullOrEmpty(update.Text)) { await events.WriteAsync(new AgentTurnEvent.Text(update.Text), ct).ConfigureAwait(false); }
                    }
                    response = updates.ToChatResponse();
                }
                else
                {
                    response = await client.GetResponseAsync(messages, options, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response.Text)) { await events.WriteAsync(new AgentTurnEvent.Text(response.Text), ct).ConfigureAwait(false); }
                }
                ct.ThrowIfCancellationRequested();
                if (response.Usage is not null) { (usage ??= new()).Add(response.Usage); }
                if (response.Messages.Count == 0) { throw new InvalidOperationException("The provider returned no messages."); }
                messages.AddRange(response.Messages);
                generated.AddRange(response.Messages);
                var requested = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToArray();
                if (requested.Length == 0)
                {
                    await events.WriteAsync(new AgentTurnEvent.Completed(generated.Select(InferenceMapping.FromChatMessage).ToArray(),
                        usage is null ? null : new(usage.InputTokenCount, usage.OutputTokenCount, usage.TotalTokenCount)), ct).ConfigureAwait(false);
                    await events.WriteAsync(new AgentTurnEvent.Finished(), ct).ConfigureAwait(false);
                    return;
                }
                foreach (var call in requested)
                {
                    var tool = tools.SingleOrDefault(t => t.Name == call.Name) ?? throw new InvalidOperationException("The model requested an unavailable tool.");
                    if (string.IsNullOrWhiteSpace(call.CallId) || !calls.Add(call.CallId)) { throw new InvalidOperationException("The model reused or omitted a tool call identity."); }
                    currentCall = call.CallId;
                    await WriteObserved(events, new AgentTurnEvent.ToolStarted(call.CallId, call.Name, JsonSerializer.Serialize(call.Arguments)), ct).ConfigureAwait(false);
                    object? result;
                    try { result = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments ?? new Dictionary<string, object?>()), ct).ConfigureAwait(false); }
                    catch
                    {
                        await events.WriteAsync(new AgentTurnEvent.ToolFailed(call.CallId, call.Name), ct).ConfigureAwait(false);
                        throw;
                    }
                    finally { currentCall = null; }
                    ct.ThrowIfCancellationRequested();
                    var resultMessage = new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, result)]);
                    messages.Add(resultMessage);
                    generated.Add(resultMessage);
                    await events.WriteAsync(new AgentTurnEvent.ToolCompleted(call.CallId, call.Name, JsonSerializer.Serialize(result)), ct).ConfigureAwait(false);
                }
            }
            throw new InvalidOperationException("The agent reached its model-call budget.");
        }
        catch (Exception error)
        {
            var failure = new AgentTurnEvent.Failed(error is OperationCanceledException ? "The run was interrupted." : error.Message,
                error is OperationCanceledException);
            if (ct.IsCancellationRequested)
            {
                if (!events.TryWrite(failure)) { events.TryComplete(error); }
            }
            else
            {
                try { await events.WriteAsync(failure, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { events.TryComplete(error); }
            }
        }
        finally
        {
            try
            {
                foreach (var session in sessions.AsEnumerable().Reverse())
                {
                    try { await session.DisposeAsync().ConfigureAwait(false); }
                    catch { /* Cleanup must not replace the already recorded execution outcome. */ }
                }
            }
            finally { events.TryComplete(); }
        }
    }

    private static async Task WriteObserved(ChannelWriter<AgentTurnEvent> events, AgentTurnEvent item, CancellationToken ct)
    {
        // The agent persists the selected model/tool-start before requesting the next event.
        // Do not begin external work before that write has succeeded.
        item.Observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await events.WriteAsync(item, ct).ConfigureAwait(false);
        await item.Observed.Task.WaitAsync(ct).ConfigureAwait(false);
    }
}