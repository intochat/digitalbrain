using System.ComponentModel;
using DigitalBrain.Core.Sdk;

namespace DigitalBrain.Google;

[Alias("DigitalBrain.Google.IGmailNeuron")]
public interface IGmailNeuron : IAgent
{
    static string IAgent.AgentDisplayName => "Gmail";

    static string IAgent.AgentDescription =>
        "List, read, and send Gmail messages for the authenticated Google account.";

    static string[] IAgent.AgentCapabilities =>
        ["gmail", "email", "google", "list", "read", "send"];

    static string IAgent.AgentInstructions => """
        You are Gmail, the email specialist. List, read, and send messages via the connected Google account.
        Sending mutates the user's mailbox — confirm intent before SendMessageAsync.
        """;

    [Description("List messages matching a Gmail search query, up to maxResults.")]
    [Alias("ListMessagesAsync")]
    Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default);

    [Description("Read a single message's body by its Gmail message id.")]
    [Alias("ReadMessageAsync")]
    Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default);

    [Description("Send an email. Mutates the user's mailbox.")]
    [Alias("SendMessageAsync")]
    Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default);
}
