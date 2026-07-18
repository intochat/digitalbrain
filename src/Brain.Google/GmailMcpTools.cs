using System.ComponentModel;
using Brain.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Google;

public static class GmailMcpTools
{
    public const string ListToolName = "gmail_list_messages";
    public const string SendToolName = "gmail_send_message";

    public static IReadOnlyList<AITool> CreateTypedTools(
        IGmailMcpClient readClient,
        IGmail commandNeuron,
        Func<SynapseMetadata> metadataFactory)
    {
        ArgumentNullException.ThrowIfNull(readClient);
        ArgumentNullException.ThrowIfNull(commandNeuron);
        ArgumentNullException.ThrowIfNull(metadataFactory);

        return
        [
            AIFunctionFactory.Create(
                [Description("List Gmail messages matching a query.")]
                (string query, int maxResults, CancellationToken cancellationToken) =>
                    readClient.ListMessagesAsync(query, maxResults, cancellationToken),
                ListToolName,
                "List Gmail messages matching a query."),
            AIFunctionFactory.Create(
                [Description("Send a Gmail message through the durable Gmail neuron command path.")]
                async (string to, string subject, string body, CancellationToken cancellationToken) =>
                {
                    var metadata = metadataFactory();
                    var command = new CommandSynapse<GmailSendRequest>(
                        metadata,
                        new GmailSendRequest(to, subject, body));
                    return await commandNeuron.SendMessageAsync(command);
                },
                SendToolName,
                "Send a Gmail message through the durable Gmail neuron command path."),
        ];
    }

    public static ChatClientAgent CreateAgent(
        IChatClient chatClient,
        IGmailMcpClient readClient,
        IGmail commandNeuron,
        Func<SynapseMetadata> metadataFactory) =>
        chatClient.AsAIAgent(
            instructions: "Assist with Gmail using only the typed Gmail tools.",
            name: "gmail-agent",
            description: "Typed Gmail MCP agent",
            tools: CreateTypedTools(readClient, commandNeuron, metadataFactory).ToList());
}
