using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.AI;
using DigitalBrain.Core.Programming;

namespace IntoChat;

public sealed class ProgramAgents(IGrainFactory grains) : IProgramRunLifecycle
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaximumAgents = 16;

    public async Task<JsonElement> ExecuteAsync(ProgramExecutionContext context, JsonElement config, CancellationToken cancellationToken)
    {
        var mode = config.GetProperty("mode").GetString();
        if (mode == "spawn")
        {
            if (config.TryGetProperty("items", out var items))
            {
                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaximumAgents)
                {
                    throw new ArgumentException($"Create agent items must be an array of at most {MaximumAgents} values.");
                }
                var references = new List<NeuronId>();
                var index = 0;
                foreach (var item in items.EnumerateArray())
                {
                    var itemConfig = ProgramExpressions.Resolve(context.Node.Config, context.Input, item, context.Outputs);
                    references.Add(await SpawnAsync(context, itemConfig, index++.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken));
                }
                return JsonSerializer.SerializeToElement(references, Json);
            }
            return JsonSerializer.SerializeToElement(await SpawnAsync(context, config, "single", cancellationToken), Json);
        }

        var target = config.TryGetProperty("agent", out var supplied) ? supplied : context.Value;
        var many = target.ValueKind == JsonValueKind.Array;
        var referencesToUse = many ? target.EnumerateArray().ToArray() : [target];
        if (referencesToUse.Length > MaximumAgents) { throw new ArgumentException($"One agent step handles at most {MaximumAgents} agents."); }
        var results = await Task.WhenAll(referencesToUse.Select(reference => ExecuteOnAgentAsync(context, config, reference, mode!, cancellationToken)));
        return many ? JsonSerializer.SerializeToElement(results, Json) : results[0];
    }

    private async Task<NeuronId> SpawnAsync(ProgramExecutionContext context, JsonElement config, string suffix, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var builder = grains.GetGrain<IAgentBuilder>(BuilderName(context.ProgramId, context.RunId));
        var key = (ReadText(config, "key") ?? context.Node.Id) + "/" + suffix;
        var model = Selection(config);
        var tools = config.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(item => item.GetString() ?? throw new ArgumentException("Every tool must be a name.")).ToArray() : [];
        var retained = config.TryGetProperty("retain", out var retain) && retain.ValueKind == JsonValueKind.True
            || ReadText(config, "lifetime") == "workspace";
        var reference = await builder.Build(new(Command(context, "build/" + suffix), key,
            ReadText(config, "instructions") ?? "Complete the supplied task accurately and concisely.", model, tools,
            ReadText(config, "prompt"), ReadText(config, "name"), $"program:{context.ProgramId}/{context.RunId}", retained));
        // Build is durable even if its caller disappears. A terminal run owns cleanup.
        cancellationToken.ThrowIfCancellationRequested();
        return NeuronId.FromGrainId(reference.GetGrainId());
    }

    private async Task<JsonElement> ExecuteOnAgentAsync(ProgramExecutionContext context, JsonElement config, JsonElement reference,
        string mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var address = Address(reference);
        var agent = grains.GetGrain<IAgent>(address.ToGrainId());
        if (mode == "stop")
        {
            return JsonSerializer.SerializeToElement(await agent.Cancel(new(Command(context, "stop/" + address), ReadText(config, "taskId"))), Json);
        }
        AgentResponse? task;
        string taskId;
        if (mode == "send")
        {
            taskId = ReadText(config, "taskId") ?? Command(context, "task/" + address).ToString();
            task = await agent.Submit(new(Command(context, "send/" + address), ReadText(config, "prompt")
                ?? throw new ArgumentException("Message agent requires a prompt."), taskId));
            if (config.TryGetProperty("wait", out var wait) && wait.ValueKind == JsonValueKind.False)
            {
                return JsonSerializer.SerializeToElement(task, Json);
            }
        }
        else if (mode == "collect")
        {
            var snapshot = await agent.GetState();
            taskId = ReadText(config, "taskId") ?? snapshot.InitialTaskId
                ?? snapshot.Tasks.LastOrDefault()?.TaskId ?? throw new ArgumentException("This agent has no task to collect. Send it a message first.");
        }
        else { throw new ArgumentException($"Unknown agent operation '{mode}'."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            task = await agent.WaitForResponse(taskId, deadline.Token);
            if (task.Status != "Completed" && mode != "collect") { throw new InvalidOperationException($"Agent task {task.Status}: {task.Error ?? "No result was produced."}"); }
            return JsonSerializer.SerializeToElement(task, Json);
        }
        catch (OperationCanceledException)
        {
            await agent.Cancel(new(Command(context, "cancel/" + address + "/" + taskId), taskId));
            if (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("Waiting for the agent exceeded five minutes."); }
            throw;
        }
    }

    public async Task FinishedAsync(ProgramDefinition definition, ProgramRunSnapshot run, CancellationToken cancellationToken)
    {
        if (!definition.Nodes.Any(node => node.Config.ValueKind == JsonValueKind.Object
            && (ReadText(node.Config, "mode") == "spawn" || ReadText(node.Config, "interface") == "agent-builder"))) { return; }
        var builder = grains.GetGrain<IAgentBuilder>(BuilderName(run.ProgramId, run.RunId));
        await builder.Close(new(StableCommand($"{run.ProgramId}/{run.RunId}/close")));
    }

    internal static AgentModelSelection Selection(JsonElement config) => new(
        ReadText(config, "modelProfile"), ReadText(config, "provider"), ReadText(config, "model"),
        ReadText(config, "reasoning"), config.TryGetProperty("maxOutputTokens", out var limit) && limit.TryGetInt32(out var tokens) ? tokens : null,
        Capabilities(ReadText(config, "capabilities")));

    internal static LlmCapabilities? Capabilities(string? text) => string.IsNullOrWhiteSpace(text) ? null
        : Enum.TryParse<LlmCapabilities>(text, ignoreCase: true, out var value) ? value
        : throw new ArgumentException("Capabilities must name Tools, Vision or StructuredOutput, separated by commas.");

    internal static NeuronId Address(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && NeuronId.TryParse(value.GetString(), out var address) && address.Type == "agent") { return address; }
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("type", out var type)
            && value.TryGetProperty("name", out var name) && type.GetString() == "agent" && name.ValueKind == JsonValueKind.String)
        {
            return new("agent", name.GetString()!);
        }
        throw new ArgumentException("Provide an agent reference returned by Create agent, or its agent:name address.");
    }

    internal static string BuilderName(string program, string run) => $"program/{program}/{run}";
    private static CommandId Command(ProgramExecutionContext context, string operation)
        => StableCommand($"{context.ProgramId}/{context.RunId}/{context.Version}/{context.Node.Id}/{operation}");
    internal static CommandId StableCommand(string text) => new(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16)));
    private static string? ReadText(JsonElement value, string name)
        => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
