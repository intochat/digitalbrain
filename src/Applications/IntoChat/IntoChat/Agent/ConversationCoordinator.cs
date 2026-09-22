using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.Agents;
using DigitalBrain.AI.Conversations;
using DigitalBrain.Contracts;
using IntoChat.Workspace.Queries;

namespace IntoChat.Agent;

internal sealed class ConversationCoordinator(IDigitalBrain brain, IAgentTurnRunner runner)
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
                await foreach (var item in runner.RunAsync(new("workspace-assistant", run, scope, state.Turns, message, null,
                    "Use supabase_schema to discover tables, then show_supabase_query_table to display requested data. Use read-only SQL. A query result opens an interactive window; never fabricate data or result identifiers. For C# behavior requests use code_contracts, then read/save/check a draft. Deploy only a passing artifact when the user requested execution. Read current revisions before mutations. Never claim a behavior is running until behavior_read reports readiness.",
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
                                var result = JsonSerializer.Deserialize<QueryWindowResult>(tool.Result, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                                results.Add(result.WindowId);
                            }
                            await emit(new { type = "TOOL_CALL_RESULT", toolCallId = tool.CallId, messageId = tool.CallId + "-result", role = "tool", content = tool.Result });
                            break;
                        case AgentTurnEvent.Failed failed: throw new InvalidOperationException(failed.Message);
                        case AgentTurnEvent.Finished: finished = true; break;
                    }
                }
                if (!finished) { throw new InvalidOperationException("The model run did not finish."); }
                ct.ThrowIfCancellationRequested();
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
