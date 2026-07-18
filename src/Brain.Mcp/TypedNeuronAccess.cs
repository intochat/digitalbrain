using Brain.Contracts;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace Brain.Mcp;

public interface ITypedNeuronAccess
{
    Task<CommandReceipt> StartGroupChatDiscussionAsync(string instanceId, CommandSynapse<StartDiscussion> command);
    Task<CommandReceipt> ApplyGroupChatUiActionAsync(string instanceId, CommandSynapse<UiActionRequest> command);
    Task<CommandReceipt> ListGmailMessagesAsync(string instanceId, CommandSynapse<GmailListRequest> command);
    Task<CommandReceipt> SendGmailMessageAsync(string instanceId, CommandSynapse<GmailSendRequest> command);
    Task<CommandReceipt> QuerySalesforceRecordsAsync(string instanceId, CommandSynapse<SalesforceQueryRequest> command);
    Task<CommandReceipt> UpdateSalesforceRecordAsync(string instanceId, CommandSynapse<SalesforceUpdateRequest> command);
}

public sealed class ClusterTypedNeuronAccess(IClusterClient clusterClient) : ITypedNeuronAccess
{
    private Brain.Client.Brain BrainClient => new(clusterClient);

    public Task<CommandReceipt> StartGroupChatDiscussionAsync(string instanceId, CommandSynapse<StartDiscussion> command) =>
        BrainClient.Get<IGroupChat>(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            instanceId).StartDiscussionAsync(command);

    public Task<CommandReceipt> ApplyGroupChatUiActionAsync(string instanceId, CommandSynapse<UiActionRequest> command) =>
        BrainClient.Get<IGroupChat>(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            instanceId).ApplyUiActionAsync(command);

    public Task<CommandReceipt> ListGmailMessagesAsync(string instanceId, CommandSynapse<GmailListRequest> command) =>
        BrainClient.Get<IGmail>(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            instanceId).ListMessagesAsync(command);

    public Task<CommandReceipt> SendGmailMessageAsync(string instanceId, CommandSynapse<GmailSendRequest> command) =>
        BrainClient.Get<IGmail>(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            instanceId).SendMessageAsync(command);

    public Task<CommandReceipt> QuerySalesforceRecordsAsync(string instanceId, CommandSynapse<SalesforceQueryRequest> command) =>
        BrainClient.Get<ISalesforce>(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            instanceId).QueryRecordsAsync(command);

    public Task<CommandReceipt> UpdateSalesforceRecordAsync(string instanceId, CommandSynapse<SalesforceUpdateRequest> command) =>
        BrainClient.Get<ISalesforce>(
            JournalOutboxFeedCommandPath.OrganizationId,
            JournalOutboxFeedCommandPath.SpaceId,
            instanceId).UpdateRecordAsync(command);
}
