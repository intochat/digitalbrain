using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI;
using DigitalBrain.AI.Conversations;
using DigitalBrain.Contracts;
using IntoChat.Workspace.Queries;

namespace IntoChat.Agent;

internal sealed class ConversationCoordinator(IDigitalBrain brain, IAgentTurnRunner runner, IConfiguration configuration)
{
    private readonly string _runtime = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);
    public static string Key(string scope, string thread) => "conversation-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { scope, thread })))).ToLowerInvariant();

    public async Task Run(string scope, string thread, string run, string message, Func<object, Task> emit, CancellationToken ct)
    {
        var key = Key(scope, thread);
        if (!_active.TryAdd(key, 0)) { throw new InvalidOperationException("This conversation already has an active run."); }
        var conversation = brain.Get<IConversation>(key);
        var begun = false;
        try
        {
            var state = await conversation.Read().WaitAsync(ct);
            ct.ThrowIfCancellationRequested();
            state = await conversation.Begin(run, message, state.Revision, _runtime);
            begun = state.ActiveRunId == run;
            await emit(new { type = "RUN_STARTED", threadId = thread, runId = run });
            var messageId = run + "-reply";
            await emit(new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" });
            var replay = state.Turns.SingleOrDefault(t => t.RunId == run);
            var text = new StringBuilder();
            var results = new List<string>();
            if (replay is not null)
            {
                await emit(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = replay.AssistantText });
            }
            else
            {
                var finished = false;
                string? queryError = null;
                var model = configuration["IntoChat:Assistant:Model"] is { Length: > 0 } modelName ? new AgentModelSelection(Model: modelName) : null;
                await foreach (var item in runner.RunAsync(new("workspace-assistant", run, scope, state.Turns, message, model,
                    "You are the IntoChat workspace assistant. Choose tools that match the user's request. Only for database requests, use supabase_schema then show_supabase_query_table with read-only SQL; never fabricate data or result identifiers. For new C# behaviors, use behavior_describe to record a readable name, purpose and declared triggers/effects; use behavior_details to get its metadata revision before updating existing descriptions. If a catalog is truncated, request the needed modules separately. Resolve neurons by concrete contracts such as ITimer, never the INeuron base interface. If deployment fails, inspect behavior_logs and repair the source before trying another checked artifact. For C# behavior requests use code_contracts, then code_draft_read, code_draft_save with both source and meaningful xUnit tests, code_draft_check, and code_check_read until terminal status. Repair diagnostics and recheck before deployment. Operation IDs must be fresh UUID strings. Read current revisions before mutations. Tool results with isError=true are failures: repair the arguments or explain the configuration problem; never claim success. Deploy only a passing artifact when the user requested execution. Never use behavior_start to create a new behavior. Never claim a behavior is running until behavior_read reports readiness.",
                    ["supabase_schema", "show_supabase_query_table", .. BehaviorAgentTools.Names]), ct))
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
                            if (tool.Name == "show_supabase_query_table")
                            {
                                using var payload = JsonDocument.Parse(tool.Result);
                                if (payload.RootElement.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
                                {
                                    queryError = payload.RootElement.GetProperty("message").GetString();
                                }
                                else
                                {
                                    var result = JsonSerializer.Deserialize<QueryWindowResult>(tool.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                                    results.Add(result.WindowId);
                                    queryError = null;
                                }
                            }
                            await emit(new { type = "TOOL_CALL_RESULT", toolCallId = tool.CallId, messageId = tool.CallId + "-result", role = "tool", content = tool.Result });
                            break;
                        case AgentTurnEvent.Failed failed: throw new InvalidOperationException(failed.Message);
                        case AgentTurnEvent.Finished: finished = true; break;
                    }
                }
                if (!finished) { throw new InvalidOperationException("The model run did not finish."); }
                ct.ThrowIfCancellationRequested();
                if (queryError is not null) { throw new WorkspaceQueryException(queryError); }
                await conversation.Complete(new(run, message, text.ToString(), results));
                begun = false;
            }
            await emit(new { type = "TEXT_MESSAGE_END", messageId });
            await emit(new { type = "RUN_FINISHED", threadId = thread, runId = run });
        }
        finally
        {
            try { if (begun) { await conversation.Interrupt(run); } }
            finally { _active.TryRemove(key, out _); }
        }
    }
}

// Only carries the safe validation messages returned by the Supabase table tool.
internal sealed class WorkspaceQueryException(string message) : Exception(message);
