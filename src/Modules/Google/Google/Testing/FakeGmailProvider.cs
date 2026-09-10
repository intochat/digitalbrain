using System.Text.Json;

namespace DigitalBrain.Google;

internal sealed class FakeGmailProvider : IGmailProvider
{
    public Task<JsonElement> InvokeAsync(string tool, IReadOnlyDictionary<string, object?> arguments,
        string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GmailTokenRefresh.ValidateToken(accessToken);
        var normalized = GmailContent.Normalize(tool, arguments);
        var payload = tool switch
        {
            "search_threads" => """{"untrustedData":true,"threads":[{"id":"thread-intochat","messages":[{"id":"message-intochat","subject":"New Customer","snippet":"Please send company information.","sender":"vlad@intochat.io"}]}]}""",
            "get_thread" => """{"id":"thread-intochat","messages":[{"id":"message-intochat","subject":"New Customer","snippet":"Please send company information.","sender":"vlad@intochat.io","plaintextBody":"Please send company information."}]}""",
            "list_labels" => """{"labels":[{"labelId":"INBOX","name":"Inbox"}]}""",
            "create_draft" => """{"id":"draft-intochat","messageId":"message-draft-intochat"}""",
            _ => throw new GmailUnavailableException("This Gmail operation is not allowed."),
        };
        using var json = JsonDocument.Parse(payload);
        return Task.FromResult(GmailContent.Project(tool, json.RootElement, normalized));
    }

    public Task<string> ReadToolSchemaHashAsync(string tool, string accessToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GmailTokenRefresh.ValidateToken(accessToken);
        return Task.FromResult(GmailMcpProvider.NativeTools.Contains(tool, StringComparer.Ordinal)
            ? "fake-gmail-schema-v1" : throw new GmailUnavailableException("This Gmail operation is not allowed."));
    }
}
