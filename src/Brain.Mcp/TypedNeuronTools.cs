using System.ComponentModel;
using Brain.Contracts;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using ModelContextProtocol.Server;

namespace Brain.Mcp;

[McpServerToolType]
public sealed class TypedNeuronTools(ITypedNeuronAccess neurons, ITypedCommandPath commandPath)
{
    [McpServerTool(Name = "groupchat_start_discussion")]
    [Description("Start a group chat discussion through the typed journal/outbox/feed command path.")]
    public Task<CommandReceipt> GroupChatStartDiscussion(
        [Description("Group chat instance id")] string instanceId,
        [Description("Discussion topic")] string topic,
        [Description("Gpt grain key")] string gptKey,
        [Description("Grok grain key")] string grokKey)
    {
        var source = Source("chat.group.v1", instanceId);
        var command = commandPath.CreateCommand(new StartDiscussion(topic, gptKey, grokKey), source);
        return neurons.StartGroupChatDiscussionAsync(instanceId, command);
    }

    [McpServerTool(Name = "groupchat_apply_ui_action")]
    [Description("Apply a UiAction to a group chat surface through the typed command path.")]
    public Task<CommandReceipt> GroupChatApplyUiAction(
        [Description("Group chat instance id")] string instanceId,
        [Description("Opaque action id")] string actionId,
        [Description("Expected surface revision")] long expectedRevision)
    {
        var source = Source("chat.group.v1", instanceId);
        var command = commandPath.CreateCommand(new UiActionRequest(actionId, expectedRevision), source);
        return neurons.ApplyGroupChatUiActionAsync(instanceId, command);
    }

    [McpServerTool(Name = "gmail_list_messages")]
    [Description("List Gmail messages through the typed journal/outbox/feed command path.")]
    public Task<CommandReceipt> GmailListMessages(
        [Description("Gmail instance id")] string instanceId,
        [Description("Gmail search query")] string query,
        [Description("Maximum results")] int maxResults)
    {
        var source = Source("google.gmail.v1", instanceId);
        var command = commandPath.CreateCommand(new GmailListRequest(query, maxResults), source);
        return neurons.ListGmailMessagesAsync(instanceId, command);
    }

    [McpServerTool(Name = "gmail_send_message")]
    [Description("Send a Gmail message through the typed journal/outbox/feed command path.")]
    public Task<CommandReceipt> GmailSendMessage(
        [Description("Gmail instance id")] string instanceId,
        [Description("Recipient")] string to,
        [Description("Subject")] string subject,
        [Description("Body")] string body)
    {
        var source = Source("google.gmail.v1", instanceId);
        var command = commandPath.CreateCommand(new GmailSendRequest(to, subject, body), source);
        return neurons.SendGmailMessageAsync(instanceId, command);
    }

    [McpServerTool(Name = "salesforce_query_records")]
    [Description("Query Salesforce records through the typed journal/outbox/feed command path.")]
    public Task<CommandReceipt> SalesforceQueryRecords(
        [Description("Salesforce instance id")] string instanceId,
        [Description("SOQL query")] string soql)
    {
        var source = Source("salesforce.v1", instanceId);
        var command = commandPath.CreateCommand(new SalesforceQueryRequest(soql), source);
        return neurons.QuerySalesforceRecordsAsync(instanceId, command);
    }

    [McpServerTool(Name = "salesforce_update_record")]
    [Description("Update a Salesforce record through the typed journal/outbox/feed command path.")]
    public Task<CommandReceipt> SalesforceUpdateRecord(
        [Description("Salesforce instance id")] string instanceId,
        [Description("Object type")] string objectType,
        [Description("Record id")] string recordId,
        [Description("Field map as key=value pairs joined by ;")] string fields)
    {
        var map = fields
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);
        var source = Source("salesforce.v1", instanceId);
        var command = commandPath.CreateCommand(new SalesforceUpdateRequest(objectType, recordId, map), source);
        return neurons.UpdateSalesforceRecordAsync(instanceId, command);
    }

    private static NeuronAddress Source(string contractId, string instanceId) =>
        new(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            contractId,
            instanceId);
}
