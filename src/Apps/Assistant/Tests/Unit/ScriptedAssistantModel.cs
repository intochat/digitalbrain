using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.AI.Agents;
using DigitalBrain.Discovery.Agents;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Apps.Assistant.Tests.Unit;

// A deterministic model: a request about Supabase customers reads the schema, creates a live table
// and opens it in the workspace, finding each method first when it is not yet offered; anything
// else is echoed back.
internal sealed partial class ScriptedAssistantModel : IChatClient
{
    private const string ReadSchema = "supabase/ReadSchema";
    private const string CreateTable = "supabase.table/CreateFromQuery";
    private const string OpenWindow = "ui.workspace/Open";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var request = history.FindLastIndex(message => message.Role == ChatRole.User);
        var asked = history[request].Text;
        if (!asked.Contains("supabase", StringComparison.OrdinalIgnoreCase)) { return Reply(new ChatMessage(ChatRole.Assistant, "You said: " + asked)); }

        var turn = history.Skip(request + 1).ToList();
        var calls = turn.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToList();
        var results = turn.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToDictionary(result => result.CallId, result => result.Result);
        string? Called(string methodId) => calls.LastOrDefault(call => call.Name == NeuronToolName.Of(methodId))?.CallId;
        var callId = "call-" + (calls.Count + 1);

        var next = new[] { ReadSchema, CreateTable, OpenWindow }.FirstOrDefault(methodId => Called(methodId) is null);
        if (next is null) { return Reply(new ChatMessage(ChatRole.Assistant, "Your customers are open in a table window.")); }
        if (options?.Tools?.Any(tool => tool.Name == NeuronToolName.Of(next)) != true)
        { return Call(callId, CapabilityTools.FindTool, new() { ["query"] = Search(next) }); }

        var tableId = Called(CreateTable) is { } created ? Json(results[created]).GetProperty("NeuronId").GetString()! : "";
        return Call(callId, NeuronToolName.Of(next), next switch
        {
            ReadSchema => new() { ["neuronId"] = "default", ["query"] = new { } },
            CreateTable => new() { ["neuronId"] = "new", ["request"] = new { title = "Customers", sql = "select id, name from customers" } },
            _ => new()
            {
                ["neuronId"] = WorkspaceOf().Match(options.Instructions ?? "").Groups[1].Value,
                ["request"] = new
                {
                    operationId = "open-" + tableId,
                    windowId = tableId,
                    title = "Customers",
                    reference = new { kind = "table", neuronId = tableId },
                    expectedRevision = 0,
                },
            },
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates()) { yield return update; }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private static string Search(string methodId) => methodId switch
    {
        ReadSchema => "read the supabase schema",
        CreateTable => "create a supabase table from a query",
        _ => "open a window in the workspace",
    };

    private static JsonElement Json(object? value) => value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);

    private static Task<ChatResponse> Call(string callId, string tool, Dictionary<string, object?> arguments)
        => Reply(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, arguments)]));

    private static Task<ChatResponse> Reply(ChatMessage message) => Task.FromResult(new ChatResponse(message));

    [GeneratedRegex("ui\\.workspace neuron \"([^\"]+)\"")]
    private static partial Regex WorkspaceOf();
}
