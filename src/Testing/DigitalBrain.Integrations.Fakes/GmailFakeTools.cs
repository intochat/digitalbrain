using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace DigitalBrain.Integrations.Fakes;

[McpServerToolType]
internal sealed class GmailFakeTools
{
    [McpServerTool(Name = "search_threads", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), ReadOnly = true, Idempotent = true)]
    [Description("Lists Gmail threads matching Gmail query syntax.")]
    public static JsonElement SearchThreads(string? query = null, int pageSize = 20)
    {
        _ = pageSize;
        var sender = query?.Contains("vlad@intochat.io", StringComparison.OrdinalIgnoreCase) is true
            ? "vlad@intochat.io"
            : "lead@acme.test";
        var domain = sender[(sender.IndexOf('@') + 1)..];
        return JsonSerializer.SerializeToElement(new
        {
            threads = new[]
            {
                new
                {
                    id = domain == "intochat.io" ? "thread-intochat" : "thread-acme",
                    messages = new[]
                    {
                        new
                        {
                            id = domain == "intochat.io" ? "message-intochat" : "message-acme",
                            snippet = "Please send company information.",
                            subject = "New company inquiry",
                            sender,
                            toRecipients = new[] { "sales@example.test" },
                            ccRecipients = Array.Empty<string>(),
                            date = "2026-08-28",
                            labelIds = new[] { "INBOX", "UNREAD" },
                        },
                    },
                },
            },
            resultCountEstimate = "1",
        });
    }

    [McpServerTool(Name = "get_message", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), ReadOnly = true, Idempotent = true)]
    public static JsonElement GetMessage(string id) => JsonSerializer.SerializeToElement(new
    {
        id,
        threadId = id.Replace("message", "thread", StringComparison.Ordinal),
        subject = "New company inquiry",
        sender = id.Contains("intochat", StringComparison.OrdinalIgnoreCase) ? "vlad@intochat.io" : "lead@acme.test",
        plaintextBody = "Please send company information.",
    });

    [McpServerTool(Name = "get_thread", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), ReadOnly = true, Idempotent = true)]
    public static JsonElement GetThread(string id) => JsonSerializer.SerializeToElement(new
    {
        id,
        messages = new[] { GetMessage(id.Replace("thread", "message", StringComparison.Ordinal)) },
    });

    [McpServerTool(Name = "list_labels", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), ReadOnly = true, Idempotent = true)]
    public static JsonElement ListLabels() => JsonSerializer.SerializeToElement(new
    {
        labels = new[] { new { id = "INBOX", name = "INBOX", type = "system" } },
    });

    [McpServerTool(Name = "list_drafts", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), ReadOnly = true, Idempotent = true)]
    public static JsonElement ListDrafts() => JsonSerializer.SerializeToElement(new { drafts = Array.Empty<object>() });

    [McpServerTool(Name = "create_draft", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput))]
    public static JsonElement CreateDraft(string[]? to = null, string? subject = null, string? body = null)
        => JsonSerializer.SerializeToElement(new
        {
            id = "draft-1",
            subject = subject ?? "",
            threadId = "thread-draft-1",
            toRecipients = to ?? [],
            plaintextBody = body ?? "",
            date = "2026-08-28",
        });

    [McpServerTool(Name = "label_message", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), Idempotent = true)]
    public static JsonElement LabelMessage(string id, string[]? labelIds = null)
        => Changed(id, labelIds);

    [McpServerTool(Name = "unlabel_message", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), Idempotent = true)]
    public static JsonElement UnlabelMessage(string id, string[]? labelIds = null)
        => Changed(id, labelIds);

    [McpServerTool(Name = "label_thread", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), Idempotent = true)]
    public static JsonElement LabelThread(string id, string[]? labelIds = null)
        => Changed(id, labelIds);

    [McpServerTool(Name = "unlabel_thread", UseStructuredContent = true, OutputSchemaType = typeof(GmailMcpOutput), Idempotent = true)]
    public static JsonElement UnlabelThread(string id, string[]? labelIds = null)
        => Changed(id, labelIds);

    private static JsonElement Changed(string id, string[]? labelIds)
        => JsonSerializer.SerializeToElement(new { id, labelIds = labelIds ?? Array.Empty<string>(), success = true });
}
