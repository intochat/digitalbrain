using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Modules.Assistant.Tests.Unit;

// Exercise the explicit Supabase tools, including their real window-opening behavior.
internal sealed class ScriptedAssistantModel : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var history = messages.ToList();
        var request = history.FindLastIndex(message => message.Role == ChatRole.User);
        var asked = history[request].Text;
        if (!asked.Contains("supabase", StringComparison.OrdinalIgnoreCase)) { return Reply(new ChatMessage(ChatRole.Assistant, "You said: " + asked)); }
        var calls = history.Skip(request + 1).SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToArray();
        var next = new[] { "supabase_schema", "show_supabase_query_table" }.FirstOrDefault(name => !calls.Any(call => call.Name == name));
        if (next is null) { return Reply(new ChatMessage(ChatRole.Assistant, "Your customers are open in a table window.")); }
        if (options?.Tools?.Any(tool => tool.Name == next) != true) { throw new InvalidOperationException("Required Supabase tool is not offered: " + next); }
        Dictionary<string, object?> arguments = next == "supabase_schema"
            ? new() { ["table"] = "customers" }
            : new() { ["title"] = "Customers", ["sql"] = "select id, name from customers" };
        return Reply(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-" + calls.Length, next, arguments)]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates()) { yield return update; }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
    private static Task<ChatResponse> Reply(ChatMessage message) => Task.FromResult(new ChatResponse(message));
}
