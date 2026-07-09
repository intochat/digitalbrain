using System.ComponentModel;
using DigitalBrain.Core;
using DigitalBrain.Core.Sdk;

namespace DigitalBrain.Google;

[Alias("DigitalBrain.Google.IGmailNeuron")]
public interface IGmailNeuron : IAgent, IHandle<CapabilityInvocation>
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

    static string IAgent.AgentInvocationGrainType => "digitalbrain.google.gmail.v1";
    static string IAgent.AgentInvocationGrainKey => "gmail-capability-main";

    [Description("Checks whether the caller has an active session and a usable Google credential; if not, shows the user a login/connect surface and returns false.")]
    [Alias("EnsureConnectedAsync")]
    Task<bool> EnsureConnectedAsync(string? clientId, CancellationToken ct = default);

    [Description("List messages matching a Gmail search query, up to maxResults.")]
    [Alias("ListMessagesAsync")]
    Task<string[]> ListMessagesAsync(string query, int maxResults = 20, CancellationToken ct = default);

    [Description("List messages for the active user session identified by clientId.")]
    [Alias("ListMessagesForClientAsync")]
    Task<string[]> ListMessagesForClientAsync(string? clientId, string query, int maxResults = 20, CancellationToken ct = default);

    [Description("Read a single message's body by its Gmail message id.")]
    [Alias("ReadMessageAsync")]
    Task<string> ReadMessageAsync(string messageId, CancellationToken ct = default);

    [Description("Read a single message for the active user session identified by clientId.")]
    [Alias("ReadMessageForClientAsync")]
    Task<string> ReadMessageForClientAsync(string? clientId, string messageId, CancellationToken ct = default);

    [Description("Send an email. Mutates the user's mailbox.")]
    [Alias("SendMessageAsync")]
    Task SendMessageAsync(string to, string subject, string body, CancellationToken ct = default);
}
