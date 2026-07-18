using Brain.Contracts;
using DigitalBrain.AI;

namespace Brain.Gateway;

public sealed class UiGatewayService(ISurfaceOwnerResolver surfaceOwnerResolver)
{
    public Task<CommandReceipt> ApplyUiActionAsync(string contractId, string instanceId, string actionId, long expectedRevision)
    {
        var owner = surfaceOwnerResolver.Resolve(contractId, instanceId);
        var source = new NeuronAddress(
            DevelopmentPrincipal.OrganizationId,
            DevelopmentPrincipal.SpaceId,
            contractId,
            instanceId);
        var command = GatewayCommandFactory.CreateCommand(
            new UiActionRequest(actionId, expectedRevision),
            source);
        return owner.ApplyUiActionAsync(command);
    }

    public Task<UiSurfaceSnapshot> GetSnapshotAsync(string contractId, string instanceId) =>
        surfaceOwnerResolver.Resolve(contractId, instanceId).GetSurfaceAsync();
}
