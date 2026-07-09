using System.ComponentModel;
using DigitalBrain.Core;
using DigitalBrain.Core.Sdk;

namespace DigitalBrain.Salesforce;

[Alias("DigitalBrain.Salesforce.ISalesforceCrmNeuron")]
public interface ISalesforceCrmNeuron : IAgent, IHandle<CapabilityInvocation>
{
    static string IAgent.AgentDisplayName => "Salesforce CRM";

    static string IAgent.AgentDescription =>
        "Query Salesforce CRM records through the authenticated Salesforce account.";

    static string[] IAgent.AgentCapabilities =>
        ["salesforce", "crm", "soql", "account", "lead", "opportunity"];

    static string IAgent.AgentInstructions => """
        You are Salesforce CRM, the customer-record specialist. Use SOQL for read-only CRM lookups.
        Mutating Salesforce records requires a separate explicit confirmation path.
        """;

    static string IAgent.AgentInvocationGrainType => "digitalbrain.salesforce.crm.v1";
    static string IAgent.AgentInvocationGrainKey => "salesforce-capability-main";

    [Description("Checks whether the caller has an active session and a usable Salesforce credential; if not, shows the user a login/connect surface and returns false.")]
    [Alias("EnsureConnectedAsync")]
    Task<bool> EnsureConnectedAsync(string? clientId, CancellationToken ct = default);

    [Description("Run a read-only SOQL query and return JSON records.")]
    [Alias("QueryAsync")]
    Task<string[]> QueryAsync(string soql, CancellationToken ct = default);

    [Description("Run a read-only SOQL query for the active user session identified by clientId.")]
    [Alias("QueryForClientAsync")]
    Task<string[]> QueryForClientAsync(string? clientId, string soql, CancellationToken ct = default);

    [Description("List Salesforce Account records, up to maxResults.")]
    [Alias("ListAccountsAsync")]
    Task<string[]> ListAccountsAsync(int maxResults = 20, CancellationToken ct = default);

    [Description("List Salesforce Account records for the active user session identified by clientId.")]
    [Alias("ListAccountsForClientAsync")]
    Task<string[]> ListAccountsForClientAsync(string? clientId, int maxResults = 20, CancellationToken ct = default);
}
