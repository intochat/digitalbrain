using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Google;

public static class GmailMcpTools
{
    public const string ListToolName = "gmail_list_messages";
    public const string SendToolName = "gmail_send_message";

    public static IReadOnlyList<AITool> CreateTypedTools(IGmailMcpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return
        [
            AIFunctionFactory.Create(
                [Description("List Gmail messages matching a query.")]
                (string query, int maxResults, CancellationToken cancellationToken) =>
                    client.ListMessagesAsync(query, maxResults, cancellationToken),
                ListToolName,
                "List Gmail messages matching a query."),
            AIFunctionFactory.Create(
                [Description("Send a Gmail message with a provider idempotency key.")]
                (string to, string subject, string body, string idempotencyKey, CancellationToken cancellationToken) =>
                    client.SendMessageAsync(to, subject, body, idempotencyKey, cancellationToken),
                SendToolName,
                "Send a Gmail message with a provider idempotency key."),
        ];
    }

    public static ChatClientAgent CreateAgent(IChatClient chatClient, IGmailMcpClient mcpClient) =>
        chatClient.AsAIAgent(
            instructions: "Assist with Gmail using only the typed Gmail MCP tools.",
            name: "gmail-agent",
            description: "Typed Gmail MCP agent",
            tools: CreateTypedTools(mcpClient).ToList());
}
