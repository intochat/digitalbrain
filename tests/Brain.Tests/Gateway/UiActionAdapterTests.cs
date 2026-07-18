using Brain.Client;
using Brain.Contracts;
using Brain.Gateway;
using DigitalBrain.AI;
using Xunit;

namespace Brain.Tests.Gateway;

public sealed class UiActionAdapterTests
{
    [Fact]
    public async Task Ui_action_calls_surface_owner_with_expected_revision()
    {
        var principal = DevelopmentPrincipal.Current;
        var surfaceGrainKey = NeuronIdentity.Derive(
            typeof(IGroupChat),
            principal.OrganizationId,
            principal.SpaceId,
            "surface-chat");
        var surfaceAddress = NeuronAddress.Parse(surfaceGrainKey);
        const string actionId = "opaque-action-7";
        const long expectedRevision = 7;
        var surfaceOwner = new RecordingGroupChat();
        var resolvedKeys = new List<string>();

        await UiActionAdapter.ApplyAsync(
            surfaceGrainKey,
            actionId,
            expectedRevision,
            principal,
            key =>
            {
                resolvedKeys.Add(key);
                return surfaceOwner;
            });

        Assert.Equal([surfaceGrainKey], resolvedKeys);
        Assert.Single(surfaceOwner.ApplyUiActionCalls);
        var command = surfaceOwner.ApplyUiActionCalls[0];
        Assert.Equal(actionId, command.Payload.ActionId);
        Assert.Equal(expectedRevision, command.Payload.ExpectedRevision);
        Assert.Equal(surfaceAddress.OrganizationId, command.Metadata.OrganizationId);
        Assert.Equal(surfaceAddress.SpaceId, command.Metadata.SpaceId);
        Assert.Equal(principal.PrincipalId, command.Metadata.PrincipalId);
        Assert.Equal(principal.OrganizationId, command.Metadata.OrganizationId);
        Assert.Equal(principal.SpaceId, command.Metadata.SpaceId);
    }

    private sealed class RecordingGroupChat : IGroupChat
    {
        public List<CommandSynapse<UiActionRequest>> ApplyUiActionCalls { get; } = [];

        public Task<CommandReceipt> ApplyUiActionAsync(CommandSynapse<UiActionRequest> command)
        {
            ApplyUiActionCalls.Add(command);
            return Task.FromResult(new CommandReceipt(
                command.Metadata.CommandId,
                CommandReceiptStatus.Accepted,
                command.Payload.ExpectedRevision + 1,
                null,
                null));
        }

        public Task<CommandReceipt> StartDiscussionAsync(CommandSynapse<StartDiscussion> command) =>
            throw new NotSupportedException();

        public Task<UiSurfaceSnapshot> GetSurfaceAsync() =>
            throw new NotSupportedException();
    }
}
