using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace IntoChat;

[McpServerToolType]
public sealed class ProgramAgentTools(IGrainFactory grains, ModelProfiles models)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "agent_models"), Description("List configured LLM model choices and named model profiles before creating an agent. Includes exact provider/model IDs and supported capabilities; never returns credentials.")]
    public string Models() => JsonSerializer.Serialize(models.List(), Json);

    [McpServerTool(Name = "agent_build"), Description("Create a durable agent through AgentBuilderNeuron.Build, which returns IAgent. Supply a stable key, instructions, optional first message, selected tools and model. Returns the agent's address, ready state, pinned model and initial task ID. Same builder/key/definition returns the same agent; conflicting redefinitions fail. Standalone agents are retained for later messages until stopped.")]
    public Task<string> Build(string key, string instructions, string? initialMessage = null, string? modelProfile = null,
        string? provider = null, string? model = null, string[]? tools = null, string builder = "workspace", string? name = null,
        string? requestId = null, string? reasoning = null, int? maxOutputTokens = null, string? capabilities = null)
        => Guard(async () =>
        {
            var reference = await grains.GetGrain<IAgentBuilder>(builder).Build(new(Id(requestId), key, instructions,
                new(modelProfile, provider, model, reasoning, maxOutputTokens, ProgramAgents.Capabilities(capabilities)),
                tools, initialMessage, name, "builder:" + builder, Retain: true));
            return await reference.GetState();
        });

    [McpServerTool(Name = "agent_send"), Description("Send a task or follow-up to a built agent using its agent:name address. Its conversation memory and selected LLM are retained. Supply requestId for safe retries. wait=true waits for the result; false returns the queued task for later agent_read.")]
    public Task<string> Send(string agent, string message, bool wait = true, string? requestId = null, CancellationToken cancellationToken = default)
        => Guard(async () =>
        {
            var target = Resolve(agent);
            var id = Id(requestId);
            var task = await target.Submit(new(id, message, id.ToString()));
            if (!wait) { return task; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                task = await target.WaitForResponse(task.TaskId, deadline.Token);
                return task;
            }
            catch (OperationCanceledException)
            {
                await target.Cancel(new(ProgramAgents.StableCommand("mcp/cancel/" + agent + "/" + task.TaskId), task.TaskId));
                if (!cancellationToken.IsCancellationRequested) { throw new TimeoutException("The agent did not finish within five minutes."); }
                throw;
            }
        });

    [McpServerTool(Name = "agent_read"), Description("Read a created agent's pinned model, instructions, available tools and task history. Supply taskId to read a specific durable task and its output/error.")]
    public Task<string> Read(string agent, string? taskId = null)
        => Guard<object?>(async () => taskId is null ? await Resolve(agent).GetState() : await Resolve(agent).GetResponse(taskId));

    [McpServerTool(Name = "agent_stop"), Description("Stop a created agent and cancel its outstanding work. Supply taskId to cancel only that task while retaining the agent for future messages.")]
    public Task<string> Stop(string agent, string? taskId = null, string? requestId = null)
        => Guard(async () => await Resolve(agent).Cancel(new(Id(requestId), taskId)));

    public IReadOnlyList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(Models, "agent_models"),
        AIFunctionFactory.Create(Build, "agent_build"),
        AIFunctionFactory.Create(Send, "agent_send"),
        AIFunctionFactory.Create(Read, "agent_read"),
        AIFunctionFactory.Create(Stop, "agent_stop"),
    ];

    private IAgent Resolve(string address) => NeuronId.TryParse(address, out var id) && id.Type == "agent"
        ? grains.GetGrain<IAgent>(id.ToGrainId()) : throw new ArgumentException("Use the returned agent address, in agent:name form.");
    private static CommandId Id(string? value) => value is null ? CommandId.New()
        : CommandId.TryParse(value, out var id) ? id : ProgramAgents.StableCommand(value);
    private static async Task<string> Guard<T>(Func<Task<T>> action)
    {
        try { return JsonSerializer.Serialize(await action(), Json); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or TimeoutException or JsonException)
        {
            throw new McpException(error.Message);
        }
    }
}
