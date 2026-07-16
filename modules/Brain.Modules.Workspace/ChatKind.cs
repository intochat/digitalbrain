using System.Text.Json;
using Brain.Contracts;

namespace Brain.Modules.Workspace;

public sealed class ChatKind : INeuronKind
{
    public string Kind => "chat";
    public string[] Contracts => ["chat.post.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "chat.post.v1" => HandlePostAsync(context, invocation),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    private ValueTask<KindResult> HandlePostAsync(NeuronContext context, NeuronInvocation invocation)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(invocation.InputJson);
        }
        catch (JsonException)
        {
            throw new BrainException("input.invalid", "malformed json");
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
                throw new BrainException("input.invalid", "text field is required");

            var text = textElement.GetString();

            if (string.IsNullOrWhiteSpace(text))
                throw new BrainException("input.invalid", "text cannot be empty");

            var textBytes = System.Text.Encoding.UTF8.GetByteCount(text);
            if (textBytes > 8192)
                throw new BrainException("input.invalid", "text exceeds maximum size of 8192 bytes");

            var now = DateTimeOffset.UtcNow;
            var eventPayload = JsonSerializer.Serialize(new { text, at = now.ToString("O") });

            var output = JsonSerializer.Serialize(new { revision = context.Revision + 1 });
            var events = new[] { ("chat.message", eventPayload) };

            return ValueTask.FromResult(new KindResult(output, events));
        }
    }

    public string Project(NeuronContext context, string projection)
    {
        var messages = new List<object>();

        foreach (var evt in context.Journal)
        {
            if (evt.Kind == "chat.message")
            {
                using var doc = JsonDocument.Parse(evt.PayloadJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("text", out var textElem) && root.TryGetProperty("at", out var atElem))
                {
                    messages.Add(new
                    {
                        text = textElem.GetString(),
                        at = atElem.GetString()
                    });
                }
            }
        }

        return JsonSerializer.Serialize(new { messages });
    }
}
