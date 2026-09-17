using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.AI.Web;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.AI;

[GrainType("agent-task")]
internal sealed class AgentTaskNeuron(NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<AgentTaskResult>> state,
    [PersistentState("typed-invocations", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AgentInvocationLedger> invocations)
    : Neuron<AgentTaskResult>(runtime, state)
{
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != AgentLifecycle.Start || State is not null) { return; }
        var start = JsonSerializer.Deserialize<AgentTaskStart>(delivery.Signal.Body, AgentLifecycle.Json)
            ?? throw new ArgumentException("Agent task start is empty.");
        if (delivery.Source != start.AgentId || Id != AgentLifecycle.Worker(start.AgentId, start.TaskId)) { return; }
        var work = await GrainFactory.GetGrain<IAgentLifecycle>(start.AgentId.ToGrainId()).Work(new(start.TaskId)).ConfigureAwait(true);
        if (work is null)
        {
            await SaveAsync(new(start.TaskId, "Cancelled", null, "The owning agent no longer has an active task.", null), cancellationToken).ConfigureAwait(true);
            return;
        }

        AgentTaskResult result;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var remaining = (work.Task.StartedAt ?? work.Task.QueuedAt) + TimeSpan.FromMinutes(3) - TimeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero) { throw new TimeoutException("The agent task exceeded its three-minute lifetime, including retries."); }
            deadline.CancelAfter(remaining);
            using var client = ServiceProvider.GetRequiredService<ModelProfiles>().CreateClient(work.Agent.Model);
            var options = ModelProfiles.CreateOptions(work.Agent.Model);
            var tools = new List<AITool>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var native = ServiceProvider.GetRequiredService<NativeTools>();
            using var invoker = new AgentInvocationScope(ServiceProvider.GetRequiredService<INeuronInvoker>(), Id, invocations, TaskScheduler.Current);
            foreach (var tool in work.Agent.Tools)
            {
                if (tool is "browse_web" or "lookup_company")
                {
                    tools.AddRange(ServiceProvider.GetRequiredService<PlaywrightWebAgent>().CreateTools(client, options)
                        .OfType<AIFunction>().Where(function => function.Name == tool));
                }
                else if (native.Contains(tool)) { tools.AddRange(native.Resolve([tool])); }
                else if (NeuronId.TryParse(tool, out var target)) { tools.AddRange(TypedNeuronFunctions.For(invoker, target, names)); }
            }
            options.Tools = tools;
            options.AllowMultipleToolCalls = false;
            if (client.GetService<FunctionInvokingChatClient>() is { } invocation)
            {
                invocation.MaximumIterationsPerRequest = 20;
                invocation.MaximumConsecutiveErrorsPerRequest = 2;
            }
            var history = JsonSerializer.Deserialize<List<ChatMessage>>(work.HistoryJson, AgentLifecycle.Json) ?? [];
            var input = new List<ChatMessage> { new(ChatRole.System, work.Agent.Instructions) };
            input.AddRange(history);
            var message = new ChatMessage(ChatRole.User, work.Task.Prompt);
            input.Add(message);
            var response = await client.GetResponseAsync(input, options, deadline.Token).ConfigureAwait(true);
            invoker.EnsureComplete();
            var output = response.Text ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(output) > 12_000) { throw new InvalidOperationException("Agent output exceeds 12 KB. Ask for a smaller answer or save a larger artifact through a tool."); }
            history.Add(message);
            history.AddRange(response.Messages);
            var historyJson = TrimHistory(history);
            result = new(start.TaskId, "Completed", output, null, historyJson);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var message = error is OperationCanceledException ? "The agent task exceeded its three-minute limit." : error.Message;
            result = new(start.TaskId, "Failed", null, message.Length > 2_000 ? message[..2_000] : message, null);
        }
        Signal completion;
        try { completion = Signal.Create(AgentLifecycle.Completed, JsonSerializer.Serialize(result, AgentLifecycle.Json)); }
        catch (SignalRejectedException)
        {
            result = new(start.TaskId, "Failed", null, "The agent result exceeds the durable delivery limit. Request a smaller answer or fewer tool results.", null);
            completion = Signal.Create(AgentLifecycle.Completed, JsonSerializer.Serialize(result, AgentLifecycle.Json));
        }
        Announce(completion, start.AgentId);
        await SaveAsync(result, cancellationToken).ConfigureAwait(true);
    }

    private static string TrimHistory(List<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            for (var index = 0; index < message.Contents.Count; index++)
            {
                if (message.Contents[index] is FunctionResultContent tool)
                {
                    var json = JsonSerializer.Serialize(tool.Result, AgentLifecycle.Json);
                    if (Encoding.UTF8.GetByteCount(json) > 3_000)
                    {
                        message.Contents[index] = new FunctionResultContent(tool.CallId, new
                        {
                            historyCompacted = true,
                            observation = json[..Math.Min(json.Length, 1_500)],
                            note = "The prior tool result was shortened to fit agent memory. Retrieve the source again before relying on omitted fields.",
                        });
                    }
                }
            }
        }
        while (true)
        {
            var json = JsonSerializer.Serialize(messages, AgentLifecycle.Json);
            if (Encoding.UTF8.GetByteCount(json) <= 28_000) { return json; }
            var nextTurn = messages.FindIndex(1, static message => message.Role == ChatRole.User);
            if (nextTurn < 1) { throw new InvalidOperationException("The latest agent turn exceeds its 28 KB memory limit. Reduce tool output or start a narrower task."); }
            messages.RemoveRange(0, nextTurn);
        }
    }
}
