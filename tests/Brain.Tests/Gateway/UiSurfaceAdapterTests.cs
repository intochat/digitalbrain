using Brain.Client;
using Brain.Contracts;
using Brain.Gateway;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Xunit;

namespace Brain.Tests.Gateway;

public sealed class UiSurfaceAdapterTests
{
    [Fact]
    public async Task Surface_snapshot_routes_to_named_typed_owner()
    {
        var principal = DevelopmentPrincipal.Current;
        var surfaceGrainKey = NeuronIdentity.Derive(
            typeof(IGroupChat),
            principal.OrganizationId,
            principal.SpaceId,
            "surface-chat");
        var expectedSnapshot = new UiSurfaceSnapshot(
            new UiSurface(
                surfaceGrainKey,
                Revision: 3,
                [new UiBlock("text", "chat-surface", [])]));
        var groupChat = new RecordingGroupChat(expectedSnapshot);
        var gmail = new RecordingGmail();
        var salesforce = new RecordingSalesforce();
        var groupChatResolvedKeys = new List<string>();
        var gmailResolvedKeys = new List<string>();
        var salesforceResolvedKeys = new List<string>();

        var snapshot = await UiSurfaceAdapter.GetAsync(
            surfaceGrainKey,
            principal,
            key =>
            {
                groupChatResolvedKeys.Add(key);
                return groupChat;
            },
            key =>
            {
                gmailResolvedKeys.Add(key);
                return gmail;
            },
            key =>
            {
                salesforceResolvedKeys.Add(key);
                return salesforce;
            });

        Assert.Equal([surfaceGrainKey], groupChatResolvedKeys);
        Assert.Empty(gmailResolvedKeys);
        Assert.Empty(salesforceResolvedKeys);
        Assert.Equal(1, groupChat.GetSurfaceCalls);
        Assert.Equal(0, gmail.GetSurfaceCalls);
        Assert.Equal(0, salesforce.GetSurfaceCalls);
        Assert.Equal(expectedSnapshot, snapshot);
    }

    private sealed class RecordingGroupChat(UiSurfaceSnapshot snapshot) : IGroupChat
    {
        public int GetSurfaceCalls { get; private set; }

        public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command) =>
            throw new NotSupportedException();

        public Task<CommandReceipt> StartDiscussionAsync(CommandSynapse<StartDiscussion> command) =>
            throw new NotSupportedException();

        public Task<UiSurfaceSnapshot> GetSurfaceAsync()
        {
            GetSurfaceCalls++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingGmail : IGmail
    {
        public int GetSurfaceCalls { get; private set; }

        public Task<string> GetIdentityAsync() =>
            throw new NotSupportedException();

        public Task<CommandReceipt> ListMessagesAsync(CommandSynapse<GmailListRequest> command) =>
            throw new NotSupportedException();

        public Task<CommandReceipt> SendMessageAsync(CommandSynapse<GmailSendRequest> command) =>
            throw new NotSupportedException();

        public Task<UiSurfaceSnapshot> GetSurfaceAsync()
        {
            GetSurfaceCalls++;
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingSalesforce : ISalesforce
    {
        public int GetSurfaceCalls { get; private set; }

        public Task<string> GetIdentityAsync() =>
            throw new NotSupportedException();

        public Task<CommandReceipt> QueryRecordsAsync(CommandSynapse<SalesforceQueryRequest> command) =>
            throw new NotSupportedException();

        public Task<CommandReceipt> UpdateRecordAsync(CommandSynapse<SalesforceUpdateRequest> command) =>
            throw new NotSupportedException();

        public Task<UiSurfaceSnapshot> GetSurfaceAsync()
        {
            GetSurfaceCalls++;
            throw new NotSupportedException();
        }
    }
}
