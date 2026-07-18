using Brain.Contracts;
using DigitalBrain.AI;

namespace Brain.Gateway;

public sealed class UiGatewayService(ISurfaceOwner surfaceOwner)
{
    public Task<CommandReceipt> ApplyUiActionAsync(string actionId, long expectedRevision, NeuronAddress source)
    {
        var command = GatewayCommandFactory.CreateCommand(
            new UiActionRequest(actionId, expectedRevision),
            source);
        return surfaceOwner.ApplyUiActionAsync(command);
    }

    public Task<UiSurfaceSnapshot> GetSnapshotAsync() => surfaceOwner.GetSurfaceAsync();
}
