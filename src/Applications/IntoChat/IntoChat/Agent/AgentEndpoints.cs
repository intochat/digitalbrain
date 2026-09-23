using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Metering;
using DigitalBrain.Compute;
using DigitalBrain.Contracts;
using DigitalBrain.Receipts;
using IntoChat.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IntoChat.Agent;

internal static class AgentEndpoints
{
    private const string ProductInstructions = "You are the IntoChat workspace assistant. Choose tools that match the user's request. Only for database requests, use supabase_schema then show_supabase_query_table with read-only SQL; refine the same window with table_refine and answer counts or aggregates with table_read instead of opening a new window. To collect typed input (a card with fields, a sign-up, a password), call show_form with fields whose kind comes from the type catalog; then answer with the handle, never re-ask for values the form collects. Use show_view to reopen a form window. Never fabricate data or result identifiers. Tool results with isError=true are failures: repair the arguments or explain the configuration problem; never claim success.";
    private const string DeveloperInstructions = "You are the IntoChat workspace assistant. Choose tools that match the user's request. Only for database requests, use supabase_schema then show_supabase_query_table with read-only SQL; refine the same window with table_refine and answer counts or aggregates with table_read instead of opening a new window. Never fabricate data or result identifiers. For new C# behaviors, use behavior_describe to record a readable name, purpose and declared triggers/effects; use behavior_details to get its metadata revision before updating existing descriptions. If a catalog is truncated, request the needed modules separately. Resolve neurons by concrete contracts such as ITimer, never the INeuron base interface. If deployment fails, inspect behavior_logs and repair the source before trying another checked artifact. For C# behavior requests use code_contracts, then code_draft_read, code_draft_save with both source and meaningful xUnit tests, code_draft_check, and code_check_read until terminal status. Repair diagnostics and recheck before deployment. Operation IDs must be fresh UUID strings. Read current revisions before mutations. Tool results with isError=true are failures: repair the arguments or explain the configuration problem; never claim success. Deploy only a passing artifact when the user requested execution. Never use behavior_start to create a new behavior. Never claim a behavior is running until behavior_read reports readiness.";

    public static string ConversationKey(string scope, string thread) =>
        "agent-conversation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope, thread })))).ToLowerInvariant();

    public static void MapWorkspaceAgent(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/workspaces/{workspaceId}/conversations/{threadId}", async (string workspaceId, string threadId, IDigitalBrain brain, IOptions<BasicAuthOptions> auth, CancellationToken ct) =>
        {
            if (!ValidId(workspaceId) || !ValidId(threadId)) { return Results.BadRequest(); }
            var scope = WorkspaceScope.Create(auth.Value.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, workspaceId);
            return Results.Ok(await brain.Get<IAgent>(ConversationKey(scope.Id, threadId)).ReadConversation(ct));
        });
        routes.MapPost("/agent", async (AgentInput input, HttpContext http, IDigitalBrain brain, IPriceBook priceBook, IAgentTurnRunner runner, IIntentUsageSink usage, IOptions<BasicAuthOptions> auth, IConfiguration configuration, IHostEnvironment environment) =>
        {
            if (!ValidId(input.ThreadId) || !ValidId(input.RunId) || !ValidId(input.WorkspaceId)
                || input.Messages is not { Count: 1 } || input.Messages[0].Role != "user"
                || string.IsNullOrWhiteSpace(input.Messages[0].Content) || input.Messages[0].Content.Length > 32000)
            { http.Response.StatusCode = 400; return; }
            var scope = WorkspaceScope.Create(auth.Value.Username is { Length: > 0 } owner ? owner : BasicAuthGate.DefaultLogin, input.WorkspaceId);
            http.Response.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";
            async Task Emit(object value)
            {
                await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(value) + "\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
            using var intent = IntentContext.Begin(input.RunId, scope.Id);
            using var capture = ContentCaptureScope.Begin(
                ContentCapturePolicy.IsLocalOwner(auth.Value, configuration, environment),
                ContentCapturePolicy.ClassOf(input.Messages));
            var agent = brain.Get<IAgent>(ConversationKey(scope.Id, input.ThreadId));
            var userText = input.Messages[0].Content;
            // Only the request that actually opened the active run may complete or interrupt it.
            // A rejected concurrent submission must never mutate the owner's conversation turn.
            var ownsRun = false;
            var activity = new IntentActivity();
            var outcome = ReceiptOutcome.Succeeded;
            string? failure = null;
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
                    var state = await agent.BeginConversation(new(input.RunId, userText), http.RequestAborted);
                    var replay = state.Turns.SingleOrDefault(turn => turn.RunId == input.RunId);
                    ownsRun = replay is null;
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
                        var fallback = AgentToolPolicy.UnsupportedBehaviorAuthoring(developerMode, userText);
                        if (fallback is not null)
                        {
                            await Emit(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = fallback });
                            await agent.CompleteConversation(new(input.RunId, userText, fallback, []), http.RequestAborted);
                        }
                        else
                        {
                            var text = new StringBuilder();
                            var results = new List<string>();
                            var queryError = await RunModel(scope.Id, input.RunId, userText, developerMode, state, messageId, configuration, runner, Emit, text, results, activity, http.RequestAborted);
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
                    outcome = ReceiptOutcome.Cancelled;
                }
                catch (WorkspaceQueryException error)
                {
                    outcome = ReceiptOutcome.Failed;
                    failure = error.Message;
                    if (ownsRun) { await KeepFailedTurn(error.Message); }
                    if (!http.RequestAborted.IsCancellationRequested)
                    { await Emit(new { type = "RUN_ERROR", message = "The table could not be opened: " + error.Message, code = "QUERY_INVALID" }); }
                }
                catch (Exception error)
                {
                    outcome = ReceiptOutcome.Failed;
                    failure = error.Message;
                    http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace agent run failed");
                    if (ownsRun) { await KeepFailedTurn(error.Message); }
                    if (!http.RequestAborted.IsCancellationRequested)
                    { await Emit(new { type = "RUN_ERROR", message = "The request could not be completed. Check the data connection or try again.", code = "AGENT_FAILED" }); }
                }
            }
            finally
            {
                if (ownsRun)
                {
                    try { await agent.InterruptConversation(input.RunId, CancellationToken.None); }
                    catch { /* Interrupt is idempotent; a cancelled run is already settled. */ }
                }
                // Metering is an observer: one durable batch per completed intent, and a storage
                // fault must never fail or hide the run the user already saw.
                try { await usage.FlushAsync(intent, CancellationToken.None); }
                catch (Exception error)
                { http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace agent usage flush failed"); }
                // One durable receipt per intent, from the usage batch and the captured activity.
                try
                {
                    var receipt = await AgentReceipts.TryWriteAsync(brain, priceBook, intent, scope.Id, input.ThreadId, activity, outcome, failure ?? activity.FailureExplanation);
                    if (receipt is not null && !http.RequestAborted.IsCancellationRequested)
                    {
                        await Emit(new
                        {
                            type = "RECEIPT",
                            outcome = receipt.Outcome.ToString(),
                            summary = receipt.Summary,
                            modelCalls = receipt.ModelCalls,
                            compute = receipt.ActualCompute,
                            computeUsd = ComputeUnits.ToUsd(receipt.ActualCompute),
                            shadow = receipt.ShadowPriced,
                            calls = receipt.Calls.Select(call => new { appId = call.AppId, operation = call.Operation, discovered = call.Discovered, succeeded = call.Succeeded }),
                            touched = receipt.Touched.Select(entry => new { source = entry.Source, semanticTypeId = entry.SemanticTypeId, readOnly = entry.ReadOnly, rowsRead = entry.RowsRead }),
                        });
                    }
                }
                catch (Exception error)
                { http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("IntoChat.Agent.AgentEndpoints").LogWarning(error, "Workspace agent receipt write failed"); }
            }
        });
    }

    internal static async Task<string?> RunModel(string scope, string run, string message, bool developerMode,
        AgentConversationState state, string messageId, IConfiguration configuration,
        IAgentTurnRunner runner, Func<object, Task> emit, StringBuilder text, List<string> results, IntentActivity activity, CancellationToken ct)
    {
        var finished = false;
        string? queryError = null;
        var model = configuration["IntoChat:Assistant:Model"] is { Length: > 0 } modelName ? new AgentModelSelection(Model: modelName) : null;
        var instructions = developerMode ? DeveloperInstructions : ProductInstructions;
        // A trimmed conversation summarizes the evicted turns; the model must still receive it.
        if (!string.IsNullOrEmpty(state.Summary)) { instructions += "\nEarlier conversation summary: " + state.Summary; }
        await foreach (var item in runner.RunAsync(new("workspace-assistant", run, scope, state.Turns, message, model,
            instructions,
            AgentToolPolicy.SelectTools(developerMode, BehaviorAgentTools.Names)), ct))
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
                    break;
                case AgentTurnEvent.Failed failed: throw new InvalidOperationException(failed.Message);
                case AgentTurnEvent.Finished: finished = true; break;
            }
        }
        if (!finished) { throw new InvalidOperationException("The model run did not finish."); }
        ct.ThrowIfCancellationRequested();
        return queryError;
    }

    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl) && !value.Contains('/') && !value.Contains('\\');
    private static bool IsLiveTableTool(string name) => name is "show_supabase_query_table" or "table_read" or "table_refine";
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
        activity.RecordTool(tool.Name, succeeded, title, rowsRead, message);
    }
    internal sealed record AgentInput(string WorkspaceId, string ThreadId, string RunId, IReadOnlyList<AgentMessage> Messages);
    internal sealed record AgentMessage(string Role, string Content, string? Class = null);
}

// Only carries the safe validation messages returned by the Supabase table tool.
internal sealed class WorkspaceQueryException(string message) : Exception(message);