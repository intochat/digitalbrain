using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Core.Behavior;
using Microsoft.Extensions.AI;

namespace IntoChat;

// AG-UI is an edge over the same program actors used by MCP and the workbench.
internal sealed class BehaviorConversationEndpoint(BehaviorService programs, IGrainFactory grains,
    BehaviorLiveEvents events, ILogger<BehaviorConversationEndpoint> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, byte> _activeThreads = new(StringComparer.Ordinal);

    public async Task RunAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var threadId = Guid.NewGuid().ToString("N");
        var runId = Guid.NewGuid().ToString("N");
        var ownsThread = false;
        var ownsStream = false;
        var started = false;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        try
        {
            using var request = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
            var root = request.RootElement;
            if (root.TryGetProperty("threadId", out var thread) && !string.IsNullOrWhiteSpace(thread.GetString()))
            {
                threadId = thread.GetString()!;
            }
            if (root.TryGetProperty("runId", out var run) && !string.IsNullOrWhiteSpace(run.GetString()))
            {
                runId = run.GetString()!;
            }
            BehaviorService.RequireId(runId);
            if (threadId.Length > 512)
            {
                throw new ArgumentException("Conversation identifiers may contain at most 512 characters.");
            }
            if (!root.TryGetProperty("messages", out var requestMessages) || requestMessages.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("A conversation turn needs a messages array containing a user message.");
            }
            var supplied = requestMessages.EnumerateArray()
                .Where(message => message.TryGetProperty("role", out var role) && role.GetString() == "user")
                .Select(message => new ChatMessage(ChatRole.User,
                    message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String
                        ? content.GetString() : throw new ArgumentException("User message content must be text."))).ToList();
            if (supplied.Count == 0)
            {
                throw new ArgumentException("A conversation turn needs a user message.");
            }
            var requestHash = Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(supplied.Select(message => message.Text), Json))));
            if (!_activeThreads.TryAdd(threadId, 0))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new { error = "A reply is already running in this conversation." }, cancellationToken);
                return;
            }
            ownsThread = true;
            var channel = events.Open(runId);
            ownsStream = true;

            var existing = await programs.ReadRunAsync("intochat", runId, cancellationToken);
            if (existing is not null)
            {
                RequireBinding(existing, threadId, requestHash, supplied);
            }
            var conversation = grains.GetGrain<IConversation>(threadId);
            List<ChatMessage> messages;
            JsonElement input;
            if (existing is null)
            {
                var program = await programs.ReadAsync("intochat", cancellationToken);
                if (program.Definition is null)
                {
                    try { await programs.DeployAsync(BehaviorExamples.IntoChat, 0, cancellationToken); }
                    catch (InvalidOperationException)
                    {
                        if ((await programs.ReadAsync("intochat", cancellationToken)).Definition is null) { throw; }
                    }
                }
                input = await conversation.PrepareInput(requestHash, JsonSerializer.SerializeToElement(supplied, Json));
                messages = BehaviorConversationHistory.Read(input.GetProperty("messages"));
            }
            else
            {
                input = existing.Input;
                messages = BehaviorConversationHistory.Read(input.GetProperty("messages"));
            }

            await WriteAsync(context, new { type = "RUN_STARTED", threadId, runId }, cancellationToken);
            // Admission can succeed before a disconnected caller sees its reply.
            started = existing is null || existing.Status == "Running";
            var admitted = existing ?? await programs.RunAsync("intochat", input, runId, cancellationToken);
            RequireBinding(admitted, threadId, requestHash, supplied);
            var finished = admitted.Status == "Running"
                ? programs.WaitAsync("intochat", runId, cancellationToken)
                : Task.FromResult(admitted);
            var streamed = new StringBuilder();
            var receivedResults = new HashSet<string>(StringComparer.Ordinal);
            while (!finished.IsCompleted)
            {
                while (channel.Reader.TryRead(out var item))
                {
                    Track(item, streamed, receivedResults);
                    await WriteAsync(context, item, cancellationToken);
                }
                await Task.WhenAny(finished, Task.Delay(40, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }
            var result = await finished;
            started = false;
            while (channel.Reader.TryRead(out var item))
            {
                Track(item, streamed, receivedResults);
                await WriteAsync(context, item, cancellationToken);
            }
            if (result.Status != "Completed")
            {
                throw new InvalidOperationException(result.Error ?? $"The IntoChat program ended with status {result.Status}.");
            }
            var answer = BehaviorConversationHistory.Answer(result.Output);
            if (streamed.Length == 0 || !string.Equals(streamed.ToString(), answer, StringComparison.Ordinal))
            {
                var messageId = runId + "-result";
                await WriteAsync(context, new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" }, cancellationToken);
                await WriteAsync(context, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = answer }, cancellationToken);
                await WriteAsync(context, new { type = "TEXT_MESSAGE_END", messageId }, cancellationToken);
            }

            var previousCalls = messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
                .Select(call => call.CallId).ToHashSet(StringComparer.Ordinal);
            if (result.Output.ValueKind == JsonValueKind.Object && result.Output.TryGetProperty("history", out var canonical)
                && canonical.ValueKind == JsonValueKind.Array)
            {
                messages = BehaviorConversationHistory.Read(canonical);
                await ReplayMissingToolsAsync(context, messages, previousCalls, receivedResults, runId, cancellationToken);
            }
            await conversation.CompleteRun(result);
            await WriteAsync(context, new { type = "RUN_FINISHED", threadId, runId, programVersion = result.Version }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (started)
            {
                try { await programs.CancelAsync("intochat", runId, CancellationToken.None); }
                catch (Exception error) { logger.LogWarning(error, "Could not cancel disconnected conversation run {RunId}", runId); }
            }
        }
        catch (Exception error)
        {
            logger.LogError(error, "IntoChat program run {RunId} failed", runId);
            var message = error is ArgumentException or InvalidOperationException or JsonException
                ? error.Message : "The assistant could not finish this response. Please try again.";
            await WriteAsync(context, new { type = "RUN_ERROR", code = "program_failure", message }, cancellationToken);
        }
        finally
        {
            if (ownsStream) { events.Close(runId); }
            if (ownsThread) { _activeThreads.TryRemove(threadId, out _); }
        }
    }

    private static void RequireBinding(BehaviorRunSnapshot run, string threadId, string hash, List<ChatMessage> supplied)
    {
        if (run.Input.ValueKind != JsonValueKind.Object || !run.Input.TryGetProperty("threadId", out var owner)
            || owner.GetString() != threadId)
        {
            throw new ArgumentException("This run id belongs to a different conversation. Start a new run id.");
        }
        var matches = run.Input.TryGetProperty("requestHash", out var original)
            ? original.GetString() == hash
            : run.Input.TryGetProperty("messages", out var messages)
                && BehaviorConversationHistory.Read(messages).Where(message => message.Role == ChatRole.User)
                    .TakeLast(supplied.Count).Select(message => message.Text).SequenceEqual(supplied.Select(message => message.Text), StringComparer.Ordinal);
        if (!matches)
        {
            throw new ArgumentException("This run id was already used for different user text. Start a new run id.");
        }
    }

    private static async Task ReplayMissingToolsAsync(HttpContext context, List<ChatMessage> messages,
        HashSet<string> previousCalls, HashSet<string> receivedResults, string runId, CancellationToken cancellationToken)
    {
        var calls = messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>()
            .GroupBy(call => call.CallId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        foreach (var result in messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>())
        {
            if (previousCalls.Contains(result.CallId) || receivedResults.Contains(result.CallId)) { continue; }
            if (calls.TryGetValue(result.CallId, out var call))
            {
                await WriteAsync(context, new { type = "TOOL_CALL_START", toolCallId = call.CallId, toolCallName = call.Name, parentMessageId = runId + "-result" }, cancellationToken);
                await WriteAsync(context, new { type = "TOOL_CALL_ARGS", toolCallId = call.CallId, delta = JsonSerializer.Serialize(call.Arguments, Json) }, cancellationToken);
            }
            await WriteAsync(context, new { type = "TOOL_CALL_RESULT", toolCallId = result.CallId, content = JsonSerializer.Serialize(result.Result, Json) }, cancellationToken);
            await WriteAsync(context, new { type = "TOOL_CALL_END", toolCallId = result.CallId }, cancellationToken);
        }
    }

    private static void Track(object item, StringBuilder text, HashSet<string> receivedResults)
    {
        var json = JsonSerializer.SerializeToElement(item, Json);
        if (json.TryGetProperty("type", out var type))
        {
            if (type.GetString() == "TEXT_MESSAGE_CONTENT") { text.Append(json.GetProperty("delta").GetString()); }
            if (type.GetString() == "TOOL_CALL_RESULT") { receivedResults.Add(json.GetProperty("toolCallId").GetString()!); }
        }
    }

    private static async Task WriteAsync(HttpContext context, object item, CancellationToken cancellationToken)
    {
        await context.Response.WriteAsync("data: " + JsonSerializer.Serialize(item, Json) + "\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
}
