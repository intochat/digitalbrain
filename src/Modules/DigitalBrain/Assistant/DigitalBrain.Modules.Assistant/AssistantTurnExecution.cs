using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Assistant;


// Executed inside the Assistant neuron. Owns model calls, replay, cancellation,
// retained history, tools, metering and receipts; no HTTP or Flutter dependencies.
public sealed class AssistantTurnExecution(IServiceProvider services, IGrainFactory grains)
{
    private IConfiguration Configuration => services.GetRequiredService<IConfiguration>();
    public AgentModelCatalog Models => new(services.GetRequiredService<ModelProfiles>(),
        services.GetRequiredService<IOptionsMonitor<AIOptions>>(), Configuration);

    public async IAsyncEnumerable<string> Run(string workspace, AssistantRun input,
        Func<bool, string?, Task<AgentDefinition>> define, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!ValidId(input.ThreadId) || !ValidId(input.RunId) || string.IsNullOrWhiteSpace(input.Owner)
            || string.IsNullOrWhiteSpace(input.Message) || input.Message.Length > 32000)
        { throw new ArgumentException("A valid thread, run and message of at most 32000 characters are required."); }
        var agent = grains.GetGrain<IAgent>(AssistantConversations.Key(workspace, input.ThreadId));
        var snapshot = await agent.ReadConversation(ct);
        var prepared = PrepareTurn(snapshot, input, Models);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var events = Channel.CreateBounded<string>(new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });
        var execution = Produce();
        try
        {
            await foreach (var item in events.Reader.ReadAllAsync(ct)) { yield return item; }
        }
        finally { await lifetime.CancelAsync(); await execution; }

        async Task Produce()
        {
            try
            {
                await Execute(workspace, input, agent, snapshot, prepared, define,
                    value => events.Writer.WriteAsync(JsonSerializer.Serialize(value), lifetime.Token).AsTask(), lifetime.Token);
                events.Writer.TryComplete();
            }
            catch (Exception error) { events.Writer.TryComplete(error); }
        }
    }

    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        && !value.Any(character => char.IsControl(character) || character is '/' or '\\');

    private async Task Execute(string workspace, AssistantRun input, IAgent agent, AgentConversationState snapshot,
        PreparedTurn prepared, Func<bool, string?, Task<AgentDefinition>> define, Func<object, Task> Emit, CancellationToken ct)
    {
        var configuration = Configuration;
        var runner = services.GetRequiredService<IAgentTurnRunner>();
        var priceBook = services.GetRequiredService<IPriceBook>();
        var usage = services.GetRequiredService<IIntentUsageSink>();
        var usageId = AssistantUsage.IntentId(workspace, input.ThreadId, input.RunId);
        using var intent = IntentContext.Begin(usageId, workspace);
        var userText = input.Message;
        // Only the request that actually opened the active run may complete or interrupt it.
        // A rejected concurrent submission must never mutate the owner's conversation turn.
        var ownsRun = false;
        var recordUsage = false;
        var usageRevision = DateTimeOffset.UtcNow;
        var activity = new IntentActivity();
        var outcome = AgentRunOutcome.Succeeded;
        async Task KeepFailedTurn(string failure)
        {
            try { await agent.CompleteConversation(new(input.RunId, userText, failure, []), CancellationToken.None); }
            catch { /* The run is no longer active; nothing to keep. */ }
            ownsRun = false;
        }
        try
        {
            try
            {
                // A retained response needs no model and must survive provider removal.
                // Replay the snapshot directly so concurrent history trimming cannot turn it into a fresh run.
                var state = prepared.Replay is not null ? snapshot
                    : await agent.BeginConversation(new(input.RunId, userText), ct);
                var replay = state.Turns.SingleOrDefault(turn => turn.RunId == input.RunId);
                ownsRun = replay is null;
                recordUsage = ownsRun;
                usageRevision = DateTimeOffset.UtcNow;
                await Emit(new { type = "RUN_STARTED", threadId = input.ThreadId, runId = input.RunId });
                var messageId = input.RunId + "-reply";
                await Emit(new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" });
                if (replay is not null)
                {
                    await Emit(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = replay.AssistantText });
                }
                else
                {
                    var developerMode = AgentToolPolicy.DeveloperModeEnabled(configuration["IntoChat:DeveloperMode"]);
                    var fallback = AgentToolPolicy.UnsupportedCSharpAuthoring(developerMode, userText);
                    if (fallback is not null)
                    {
                        await Emit(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = fallback });
                        await agent.CompleteConversation(new(input.RunId, userText, fallback, []), ct);
                    }
                    else
                    {
                        var text = new StringBuilder();
                        var results = new List<string>();
                        var definition = await define(developerMode, state.Summary);
                        var queryError = await RunModel(workspace, usageId, userText, definition, state, messageId, configuration, runner, Emit, text, results, activity, ct, prepared.Model);
                        if (queryError is not null) { throw new AssistantQueryException(queryError); }
                        await agent.CompleteConversation(new(input.RunId, userText, text.ToString(), results), ct);
                    }
                    ownsRun = false;
                }
                await Emit(new { type = "TEXT_MESSAGE_END", messageId });
                await Emit(new { type = "RUN_FINISHED", threadId = input.ThreadId, runId = input.RunId });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The client disconnected; the run is interrupted and its turn is dropped.
                outcome = AgentRunOutcome.Cancelled;
            }
            catch (AssistantQueryException error)
            {
                outcome = AgentRunOutcome.Failed;
                if (ownsRun) { await KeepFailedTurn(error.Message); }
                if (!ct.IsCancellationRequested)
                { await Emit(new { type = "RUN_ERROR", message = "The table could not be opened: " + error.Message, code = "QUERY_INVALID" }); }
            }
            catch (Exception error)
            {
                outcome = AgentRunOutcome.Failed;
                services.GetRequiredService<ILoggerFactory>().CreateLogger("Assistant").LogWarning(error, "Workspace agent run failed");
                if (ownsRun) { await KeepFailedTurn(error.Message); }
                if (!ct.IsCancellationRequested)
                { await Emit(new { type = "RUN_ERROR", message = "The request could not be completed. Check the data connection or try again.", code = "AGENT_FAILED" }); }
            }
        }
        finally
        {
            // Metering is an observer: one durable batch per completed intent, and a storage
            // fault must never fail or hide the run the user already saw.
            try { await usage.FlushAsync(intent, CancellationToken.None); }
            catch (Exception error)
            { services.GetRequiredService<ILoggerFactory>().CreateLogger("Assistant").LogWarning(error, "Workspace agent usage flush failed"); }
            if (ownsRun)
            {
                // Release an interrupted run only after its usage batch is durable, so a
                // retry with the same id can price the cumulative provider consumption.
                try { await agent.InterruptConversation(input.RunId, CancellationToken.None); }
                catch { /* Interrupt is idempotent; a cancelled run is already settled. */ }
            }
            ComputeUsageItem? projection = null;
            if (recordUsage)
            {
                try
                {
                    var tokens = await grains.GetGrain<IIntentUsage>(usageId).ReadAsync(CancellationToken.None);
                    projection = AssistantUsage.Create(usageId, AgentReceipts.Create(priceBook, intent, activity, outcome, tokens.Entries), priceBook, tokens.Entries);
                    await services.GetRequiredService<IUsageStore>().AppendAsync(input.Owner, workspace, usageId,
                        JsonSerializer.Serialize(projection), CancellationToken.None, usageRevision);
                }
                catch (Exception error)
                { services.GetRequiredService<ILoggerFactory>().CreateLogger("Assistant").LogWarning(error, "Workspace compute history write failed"); }
            }
            // Send a receipt card from the intent's usage and captured activity.
            try
            {
                if (recordUsage && !ct.IsCancellationRequested)
                {
                    var receipt = AgentReceipts.Create(priceBook, intent, activity, outcome, projection?.ModelUsage);
                    await Emit(new
                    {
                        type = "RECEIPT",
                        id = usageId,
                        intentId = usageId,
                        occurredAt = DateTimeOffset.UtcNow,
                        priceBookVersion = priceBook.Version,
                        previewCompute = projection?.PreviewCompute,
                        modelUsage = (projection?.ModelUsage ?? intent.Usage.OfType<TokenUsageEntry>()).Select(entry => new
                        {
                            provider = entry.Provider, model = entry.Model, inputTokens = entry.InputTokens,
                            cachedInputTokens = entry.CachedInputTokens, reasoningTokens = entry.ReasoningTokens,
                            outputTokens = entry.OutputTokens, totalTokens = entry.TotalTokens, usageReported = entry.UsageReported,
                        }),
                        outcome = receipt.Outcome.ToString(),
                        summary = receipt.Summary,
                        modelCalls = receipt.ModelCalls,
                        compute = receipt.Compute,
                        computeUsd = ComputeUnits.ToUsd(receipt.Compute),
                        shadow = true,
                        calls = receipt.Calls.Select(call => new { appId = call.AppId, operation = call.Operation, discovered = call.Discovered, succeeded = call.Succeeded }),
                        touched = receipt.Touched.Select(entry => new { source = entry.Source, semanticTypeId = entry.SemanticTypeId, readOnly = entry.ReadOnly, rowsRead = entry.RowsRead }),
                    });
                }
            }
            catch (Exception error)
            { services.GetRequiredService<ILoggerFactory>().CreateLogger("Assistant").LogWarning(error, "Workspace agent receipt emission failed"); }
        }
    }

    public static PreparedTurn PrepareTurn(AgentConversationState snapshot, AssistantRun input, AgentModelCatalog catalog)
    {
        var replay = snapshot.Turns.SingleOrDefault(turn => turn.RunId == input.RunId);
        if (replay is not null)
        {
            if (replay.UserText != input.Message) { throw new InvalidOperationException("Run ID already belongs to another message."); }
            return new(replay, null);
        }
        // Validation precedes BeginConversation: rejected choices never open or fail a turn.
        return new(null, catalog.Select(input.ModelProfile));
    }

    public sealed record PreparedTurn(AgentConversationTurn? Replay, AgentModelSelection? Model);

    public static async Task<string?> RunModel(string scope, string run, string message, AgentDefinition definition,
        AgentConversationState state, string messageId, IConfiguration configuration,
        IAgentTurnRunner runner, Func<object, Task> emit, StringBuilder text, List<string> results, IntentActivity activity, CancellationToken ct, AgentModelSelection? modelSelection = null)
    {
        var finished = false;
        string? queryError = null;
        var model = modelSelection ?? (configuration["IntoChat:Assistant:Model"] is { Length: > 0 } modelName ? new AgentModelSelection(Model: modelName) : null);
        await foreach (var item in runner.RunAsync(new("workspace-assistant", run, scope, state.Turns, message, model,
            definition.Instructions, AgentToolPolicy.ForDatabase(definition.Tools, message), ContextProviders: definition.ContextProviders), ct))
        {
            switch (item)
            {
                case AgentTurnEvent.Text delta:
                    text.Append(delta.Content);
                    await emit(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = delta.Content });
                    break;
                case AgentTurnEvent.ToolStarted tool:
                    await emit(new { type = "TOOL_CALL_START", toolCallId = tool.CallId, toolCallName = tool.Name, parentMessageId = messageId });
                    await emit(new { type = "TOOL_CALL_ARGS", toolCallId = tool.CallId, delta = tool.Arguments });
                    await emit(new { type = "TOOL_CALL_END", toolCallId = tool.CallId });
                    break;
                case AgentTurnEvent.ToolCompleted tool:
                    if (IsLiveTableTool(tool.Name))
                    {
                        using var payload = JsonDocument.Parse(tool.Result);
                        if (payload.RootElement.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
                        {
                            queryError = payload.RootElement.GetProperty("message").GetString();
                        }
                        else
                        {
                            // A successful live-table call settles any earlier table failure. Only the
                            // open tool contributes a window id; read and refine stay on that window.
                            if (tool.Name is not ("postgres_schema" or "supabase_schema")) { queryError = null; }
                            if (tool.Name is "show_supabase_query_table" or "show_postgres_query_table")
                            {
                                var window = payload.RootElement.TryGetProperty("windowId", out var camel) ? camel : payload.RootElement.GetProperty("WindowId");
                                results.Add(window.GetString()!);
                            }
                        }
                    }
                    RecordToolActivity(activity, tool);
                    await emit(new { type = "TOOL_CALL_RESULT", toolCallId = tool.CallId, messageId = tool.CallId + "-result", role = "tool", content = tool.Result });
                    await EmitUiCard(tool.Result, emit);
                    break;
                case AgentTurnEvent.Failed failed: throw new InvalidOperationException(failed.Message);
                case AgentTurnEvent.Finished: finished = true; break;
            }
        }
        if (!finished) { throw new InvalidOperationException("The model run did not finish."); }
        ct.ThrowIfCancellationRequested();
        return queryError;
    }

    private static bool IsLiveTableTool(string name) => name is "show_supabase_query_table" or "show_postgres_query_table" or "postgres_schema" or "supabase_schema" or "table_read" or "table_refine";

    private static async Task EmitUiCard(string result, Func<object, Task> emit)
    {
        try
        {
            using var payload = JsonDocument.Parse(result);
            if (payload.RootElement.ValueKind != JsonValueKind.Object || !payload.RootElement.TryGetProperty("_ui", out var card) || card.ValueKind != JsonValueKind.Object) { return; }
            await emit(new { type = "UI_CARD", card = card.Clone() });
        }
        catch (JsonException)
        {
            // A non-JSON tool result carries no UI channel card.
        }
    }
    private static void RecordToolActivity(IntentActivity activity, AgentTurnEvent.ToolCompleted tool)
    {
        var succeeded = true;
        string? title = null;
        string? message = null;
        long rowsRead = 0;
        try
        {
            using var payload = JsonDocument.Parse(tool.Result);
            var root = payload.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                succeeded = false;
                if (root.TryGetProperty("message", out var reason) && reason.ValueKind == JsonValueKind.String) { message = reason.GetString(); }
            }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("rowsRead", out var rows) && rows.ValueKind == JsonValueKind.Number && rows.TryGetInt64(out var count)) { rowsRead = count; }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("title", out var windowTitle) && windowTitle.ValueKind == JsonValueKind.String) { title = windowTitle.GetString(); }
        }
        catch (JsonException)
        {
            // A non-JSON tool result is still a call, just without a row count.
        }
        activity.RecordTool(tool.Name, succeeded, title, rowsRead);
    }
}

internal sealed class AssistantQueryException(string message) : Exception(message);
