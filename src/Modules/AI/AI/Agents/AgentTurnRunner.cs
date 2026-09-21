using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using DigitalBrain.AI.Conversations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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
    string? Instructions = null, IReadOnlyList<string>? ToolNames = null);
public abstract record AgentTurnEvent
{
    public sealed record Started(string RunId) : AgentTurnEvent;
    public sealed record Text(string Content) : AgentTurnEvent;
    public sealed record ToolStarted(string CallId, string Name, string Arguments) : AgentTurnEvent;
    public sealed record ToolCompleted(string CallId, string Name, string Result) : AgentTurnEvent;
    public sealed record Finished : AgentTurnEvent;
    public sealed record Failed(string Message) : AgentTurnEvent;
}

// Owns only per-turn closures. Production clients already contain one invocation loop;
// bare clients supplied by local tests receive that same SDK loop here.
public sealed class AgentTurnRunner(IServiceProvider services) : IAgentTurnRunner
{
    public async IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        var events = Channel.CreateUnbounded<AgentTurnEvent>(new() { SingleReader = true });
        var execution = Execute(request, events.Writer, lifetime);
        try
        {
            await foreach (var item in events.Reader.ReadAllAsync(ct).ConfigureAwait(false)) { yield return item; }
        }
        finally { lifetime.Cancel(); await execution.ConfigureAwait(false); }
    }

    private async Task Execute(AgentTurnRequest request, ChannelWriter<AgentTurnEvent> events, CancellationTokenSource lifetime)
    {
        try
        {
            var ct = lifetime.Token;
            events.TryWrite(new AgentTurnEvent.Started(request.RunId));
            AgentToolContext Context() => new(request.ScopeId, request.RunId,
                FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? throw new InvalidOperationException("Missing trusted tool call identity."));
            var selected = request.ToolNames ?? [];
            var available = selected.Count == 0 ? [] : services.GetServices<IAgentToolFactory>().SelectMany(f => f.Create(Context)).ToArray();
            Exception? toolFailure = null;
            var tools = selected.Select(name =>
            {
                var matches = available.Where(f => f.Name == name).ToArray();
                if (matches.Length != 1) { throw new InvalidOperationException($"Selected tool '{name}' must have exactly one registration."); }
                return (AITool)new ObservedFunction(matches[0], events, error => { toolFailure = error; lifetime.Cancel(); });
            }).ToList();
            IChatClient client;
            bool owned;
            if (tools.Count > 0 && services.GetService<ModelProfiles>() is { } profiles)
            {
                client = profiles.CreateClient(profiles.Resolve(request.Model, requiresTools: true));
                owned = true;
            }
            else { client = Providers.Resolve(services, request.Model?.Provider, request.Model?.Model, out owned); }
            using var ownedClient = owned ? client : null;
            if (client.GetService<FunctionInvokingChatClient>() is null)
            { client = new ChatClientBuilder(client).UseFunctionInvocation().Build(services); }
            var agent = new ChatClientAgent(client, new ChatClientAgentOptions
            {
                Name = request.AgentId,
                UseProvidedChatClientAsIs = true,
                ChatOptions = new() { Instructions = request.Instructions, Tools = tools },
            }, services: services);
            var messages = new List<ChatMessage>();
            foreach (var turn in request.History)
            {
                messages.Add(new(ChatRole.User, turn.UserText));
                messages.Add(new(ChatRole.Assistant, turn.AssistantText + (turn.ResultIds.Count == 0 ? "" : "\nResult windows: " + string.Join(", ", turn.ResultIds))));
            }
            messages.Add(new(ChatRole.User, request.Message));
            var session = await agent.CreateSessionAsync(ct).ConfigureAwait(false);
            var response = await agent.RunAsync(messages, session, cancellationToken: ct).ConfigureAwait(false);
            if (toolFailure is not null) { throw new InvalidOperationException("A required tool failed.", toolFailure); }
            // Unhandled calls must never become a successful conversational turn.
            if (response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any(c => !selected.Contains(c.Name)))
            { throw new InvalidOperationException("The model requested an unavailable tool."); }
            ct.ThrowIfCancellationRequested();
            events.TryWrite(new AgentTurnEvent.Text(response.Text ?? string.Empty));
            events.TryWrite(new AgentTurnEvent.Finished());
        }
        catch (Exception error) { events.TryWrite(new AgentTurnEvent.Failed(error is OperationCanceledException ? "The run was interrupted." : error.Message)); }
        finally { events.TryComplete(); }
    }

    private sealed class ObservedFunction(AIFunction inner, ChannelWriter<AgentTurnEvent> events, Action<Exception> failed) : DelegatingAIFunction(inner)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? throw new InvalidOperationException("Missing tool call ID.");
            events.TryWrite(new AgentTurnEvent.ToolStarted(callId, Name, JsonSerializer.Serialize(arguments)));
            try
            {
                var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
                events.TryWrite(new AgentTurnEvent.ToolCompleted(callId, Name, JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
                return result;
            }
            catch (Exception error) { failed(error); throw; }
        }
    }
}