using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Compute.Usage;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using IntoChat.Apps.BuiltIn;
using IntoChat.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IntoChat.Agent;

internal static class AgentEndpoints
{
    public static string ConversationKey(string scope, string thread) =>
        "agent-conversation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope, thread })))).ToLowerInvariant();

    public static void MapWorkspaceAgent(this IEndpointRouteBuilder routes)
    {
        // AccountSession gates this route exactly as it gates /agent, including local-owner mode.
        routes.MapGet("/ai/models", (ModelProfiles profiles, IOptionsMonitor<AIOptions> options, IConfiguration configuration) =>
            Results.Ok(new AgentModelCatalog(profiles, options, configuration).Read()));
        routes.MapGet("/workspaces/{workspaceId}/conversations/{threadId}", async (string workspaceId, string threadId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (!ValidId(workspaceId) || !ValidId(threadId)) { return Results.BadRequest(); }
            var scope = WorkspaceScope.Current(auth.Value, workspaceId);
            return Results.Ok(await (await brain.Get<IAssistantApp>(scope.Id).Conversation(threadId)).ReadConversation(ct));
        });
        routes.MapPost("/agent", async (AgentInput input, HttpContext http, IDigitalBrain brain, IPriceBook priceBook, IAgentTurnRunner runner, IIntentUsageSink usage, IOptions<BasicAuthOptions> auth, IConfiguration configuration, IHostEnvironment environment, ModelProfiles profiles, IOptionsMonitor<AIOptions> aiOptions) =>
        {
            if (!ValidId(input.ThreadId) || !ValidId(input.RunId) || !ValidId(input.WorkspaceId)
                || input.Messages is not { Count: 1 } || input.Messages[0].Role != "user"
                || string.IsNullOrWhiteSpace(input.Messages[0].Content) || input.Messages[0].Content.Length > 32000)
            { http.Response.StatusCode = 400; return; }
            if (http.User.Identity?.IsAuthenticated == true &&
                !await http.RequestServices.GetRequiredService<IWorkspaceAccess>().CanAccessAsync(CallerContextStamper.Require().PrincipalId, input.WorkspaceId, http.RequestAborted))
            { http.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
            var scope = WorkspaceScope.Current(auth.Value, input.WorkspaceId);
            var agent = await brain.Get<IAssistantApp>(scope.Id).Conversation(input.ThreadId);
            var snapshot = await agent.ReadConversation(http.RequestAborted);
            PreparedTurn prepared;
            try { prepared = PrepareTurn(snapshot, input, new AgentModelCatalog(profiles, aiOptions, configuration)); }
            catch (ArgumentException error)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new { code = "MODEL_UNAVAILABLE", message = error.Message }, http.RequestAborted);
                return;
            }
            catch (InvalidOperationException error)
            {
                http.Response.StatusCode = StatusCodes.Status409Conflict;
                await http.Response.WriteAsJsonAsync(new { code = "RUN_CONFLICT", message = error.Message }, http.RequestAborted);
                return;
            }
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            async Task Emit(object value)
            {
                await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(value) + "\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
            var usageId = ComputeUsageEndpoints.IntentId(scope.Id, input.ThreadId, input.RunId);
            using var intent = IntentContext.Begin(usageId, scope.Id);
            using var capture = ContentCaptureScope.Begin(
                ContentCapturePolicy.IsLocalOwner(auth.Value, configuration, environment),
                ContentCapturePolicy.ClassOf(input.Messages));
            var userText = input.Messages[0].Content;
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
                        : await agent.BeginConversation(new(input.RunId, userText), http.RequestAborted);
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
                            await agent.CompleteConversation(new(input.RunId, userText, fallback, []), http.RequestAborted);
                        }
                        else
                        {
                            var text = new StringBuilder();
                            var results = new List<string>();
                            var appTools = await new AgentToolSelection(brain).ResolveAsync(scope.Id, http.RequestAborted);
                            var queryError = await RunModel(scope.Id, usageId, userText, developerMode, state, messageId, configuration, runner, appTools, Emit, text, results, activity, http.RequestAborted, prepared.Model);
                            if (queryError is not null) { throw new WorkspaceQueryException(queryError); }
                            await agent.CompleteConversation(new(input.RunId, userText, text.ToString(), results), http.RequestAborted);
                        }
                        ownsRun = false;
                    }
                    await Emit(new { type = "TEXT_MESSAGE_END", messageId });
                    await Emit(new { type = "RUN_FINISHED", threadId = input.ThreadId, runId = input.RunId });
                }
                catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
                {
                    // The client disconnected; the run is interrupted and its turn is dropped.
                    outcome = AgentRunOutcome.Cancelled;
                }
                catch (WorkspaceQueryException error)
                {
                    outcome = AgentRunOutcome.Failed;
                    if (ownsRun) { await KeepFailedTurn(error.Message); }
                    if (!http.RequestAborted.IsCancellationRequested)
                    { await Emit(new { type = "RUN_ERROR", message = "The table could not be opened: " + error.Message, code = "QUERY_INVALID" }); }
                }
                catch (Exception error)
                {
                    outcome = AgentRunOutcome.Failed;
                    http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace agent run failed");
                    if (ownsRun) { await KeepFailedTurn(error.Message); }
                    if (!http.RequestAborted.IsCancellationRequested)
                    { await Emit(new { type = "RUN_ERROR", message = "The request could not be completed. Check the data connection or try again.", code = "AGENT_FAILED" }); }
                }
            }
            finally
            {
                // Metering is an observer: one durable batch per completed intent, and a storage
                // fault must never fail or hide the run the user already saw.
                try { await usage.FlushAsync(intent, CancellationToken.None); }
                catch (Exception error)
                { http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace agent usage flush failed"); }
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
                        var tokens = await brain.Get<IIntentUsage>(usageId).ReadAsync(CancellationToken.None);
                        projection = ComputeUsageEndpoints.Create(usageId, AgentReceipts.Create(priceBook, intent, activity, outcome, tokens.Entries), priceBook, tokens.Entries);
                        await http.RequestServices.GetRequiredService<IUsageStore>().AppendAsync(scope.Owner, scope.Id, usageId,
                            JsonSerializer.Serialize(projection), CancellationToken.None, usageRevision);
                    }
                    catch (Exception error)
                    { http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace compute history write failed"); }
                }
                // Send a receipt card from the intent's usage and captured activity.
                try
                {
                    if (recordUsage && !http.RequestAborted.IsCancellationRequested)
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
                { http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace agent receipt emission failed"); }
            }
        });
    }

    internal static PreparedTurn PrepareTurn(AgentConversationState snapshot, AgentInput input, AgentModelCatalog catalog)
    {
        var replay = snapshot.Turns.SingleOrDefault(turn => turn.RunId == input.RunId);
        if (replay is not null)
        {
            if (replay.UserText != input.Messages[0].Content) { throw new InvalidOperationException("Run ID already belongs to another message."); }
            return new(replay, null);
        }
        // Validation precedes BeginConversation: rejected choices never open or fail a turn.
        return new(null, catalog.Select(input.ModelProfile));
    }

    internal sealed record PreparedTurn(AgentConversationTurn? Replay, AgentModelSelection? Model);

    internal static async Task<string?> RunModel(string scope, string run, string message, bool developerMode,
        AgentConversationState state, string messageId, IConfiguration configuration,
        IAgentTurnRunner runner, IReadOnlyList<string> appTools, Func<object, Task> emit, StringBuilder text, List<string> results, IntentActivity activity, CancellationToken ct, AgentModelSelection? modelSelection = null)
    {
        var finished = false;
        string? queryError = null;
        var model = modelSelection ?? (configuration["IntoChat:Assistant:Model"] is { Length: > 0 } modelName ? new AgentModelSelection(Model: modelName) : null);
        var definition = AssistantDefinition.For(developerMode, appTools);
        var instructions = definition.Instructions;
        // A trimmed conversation summarizes the evicted turns; the model must still receive it.
        if (!string.IsNullOrEmpty(state.Summary)) { instructions += "\nEarlier conversation summary: " + state.Summary; }
        await foreach (var item in runner.RunAsync(new("workspace-assistant", run, scope, state.Turns, message, model,
            instructions, definition.Tools, ContextProviders: definition.ContextProviders), ct))
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
                            queryError = null;
                            if (tool.Name == "show_supabase_query_table")
                            {
                                var result = JsonSerializer.Deserialize<QueryWindowResult>(tool.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                                results.Add(result.WindowId);
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

    internal static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl) && !value.Contains('/') && !value.Contains('\\');
    private static bool IsLiveTableTool(string name) => name is "show_supabase_query_table" or "table_read" or "table_refine";

    private static async Task EmitUiCard(string result, Func<object, Task> emit)
    {
        try
        {
            using var payload = JsonDocument.Parse(result);
            if (!payload.RootElement.TryGetProperty("_ui", out var card) || card.ValueKind != JsonValueKind.Object) { return; }
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
            if (root.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            {
                succeeded = false;
                if (root.TryGetProperty("message", out var reason) && reason.ValueKind == JsonValueKind.String) { message = reason.GetString(); }
            }
            if (root.TryGetProperty("rowsRead", out var rows) && rows.TryGetInt64(out var count)) { rowsRead = count; }
            if (root.TryGetProperty("title", out var windowTitle) && windowTitle.ValueKind == JsonValueKind.String) { title = windowTitle.GetString(); }
        }
        catch (JsonException)
        {
            // A non-JSON tool result is still a call, just without a row count.
        }
        activity.RecordTool(tool.Name, succeeded, title, rowsRead);
    }
    internal sealed record AgentInput(string WorkspaceId, string ThreadId, string RunId, IReadOnlyList<AgentMessage> Messages, string? ModelProfile = null);
    internal sealed record AgentMessage(string Role, string Content, string? Class = null);
}

// Only carries the safe validation messages returned by the Supabase table tool.
internal sealed class WorkspaceQueryException(string message) : Exception(message);
